using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.App.Extensions;

/// <summary>
/// Helper for wiring the extension loader into an <see cref="IHostApplicationBuilder"/>.
/// </summary>
/// <remarks>
/// Discovers extension assemblies, lets each one register services, and -- once the
/// host service provider is built -- dispatches lifecycle hooks. Intended to be the
/// single entry point used by every Magpie command that hosts long-running services.
/// </remarks>
public static class MagpieHostBuilder
{
    /// <summary>
    /// Discovers and registers extensions found under the configured extensions
    /// directory and arranges for their lifecycle hooks to fire after the host starts.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The list of extensions that were successfully registered.</returns>
    public static IReadOnlyList<LoadedExtension> AddMagpieExtensions(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new ExtensionLoaderOptions();
        builder.Configuration.GetSection(ExtensionLoaderOptions.SectionName).Bind(options);

        var extensionsPath = ExtensionPathResolver.Resolve(
            options.ExtensionsPath,
            builder.Environment.ContentRootPath);

        // Build a temporary logger so failures during discovery are visible even before
        // the host's service provider exists. Console matches the default provider used
        // by the runtime host, so bootstrap and runtime logs render the same way.
        using var bootstrapFactory = LoggerFactory.Create(b =>
        {
            b.AddConfiguration(builder.Configuration.GetSection("Logging"));
            b.AddConsole();
        });
        var loader = new ExtensionLoader(bootstrapFactory.CreateLogger<ExtensionLoader>());

        var loaded = loader.LoadExtensions(extensionsPath, builder.Services);

        builder.Services.AddSingleton<IReadOnlyList<LoadedExtension>>(loaded);
        builder.Services.AddSingleton<ExtensionLifecycleDispatcher>();
        builder.Services.AddSingleton(new ExtensionHostState(extensionsPath));
        builder.Services.AddHostedService<ExtensionLifecycleHostedService>();

        return loaded;
    }
}

/// <summary>
/// Captures host-wide state that is needed when dispatching extension lifecycle hooks.
/// </summary>
/// <param name="ExtensionsRoot">The absolute path to the extensions root directory.</param>
public sealed record ExtensionHostState(string ExtensionsRoot);

/// <summary>
/// Hosted service that fires <see cref="Magpie.Abstractions.IExtensionLifecycle"/> hooks
/// for every loaded extension once the host's service provider has been built.
/// </summary>
/// <remarks>
/// The lifecycle dispatch runs in the background so it does not block host startup. The
/// host (and the API) may begin serving requests before every extension's
/// <c>OnAppInitializeAsync</c> has finished. Consumers that genuinely need lifecycle
/// completion before being usable must coordinate that themselves -- nothing in the
/// current code base relies on synchronous lifecycle completion at host start.
/// </remarks>
internal sealed class ExtensionLifecycleHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IReadOnlyList<LoadedExtension> _loaded;
    private readonly ExtensionLifecycleDispatcher _dispatcher;
    private readonly ExtensionHostState _state;
    private readonly ILogger<ExtensionLifecycleHostedService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _dispatchTask;

    public ExtensionLifecycleHostedService(
        IServiceProvider services,
        IReadOnlyList<LoadedExtension> loaded,
        ExtensionLifecycleDispatcher dispatcher,
        ExtensionHostState state,
        ILogger<ExtensionLifecycleHostedService> logger)
    {
        _services = services;
        _loaded = loaded;
        _dispatcher = dispatcher;
        _state = state;
        _logger = logger;
    }

    /// <summary>
    /// Exposes the in-flight dispatch task for tests and any caller that wants to
    /// observe completion.
    /// </summary>
    internal Task? DispatchTask => _dispatchTask;

    public Task StartAsync(System.Threading.CancellationToken cancellationToken)
    {
        // TODO: NodeId should come from IMagpieSystem once registered.
        var nodeId = Environment.MachineName;
        var context = new ExtensionLifecycleContext(nodeId, _services);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Dispatch off the host startup path. Any failures are logged inside
        // DispatchAsync's per-hook try/catch; we log a final wrapper here in case the
        // dispatch itself blew up in setup code outside individual hooks.
        _dispatchTask = Task.Run(
            async () =>
            {
                try
                {
                    await _dispatcher
                        .DispatchAsync(_loaded, _state.ExtensionsRoot, context, _cts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    // Shutdown -- nothing to do.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Extension lifecycle dispatch failed.");
                }
            },
            _cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(System.Threading.CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_dispatchTask is not null)
        {
            try
            {
                await _dispatchTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Either the lifecycle dispatch or the host shutdown was cancelled. The
                // dispatch task already swallows its own OCE, so this catch handles the
                // WaitAsync timeout case.
            }
        }

        _cts?.Dispose();
        _cts = null;
    }
}
