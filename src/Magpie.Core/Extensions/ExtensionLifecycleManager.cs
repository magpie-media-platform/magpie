using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Extensions;

internal sealed class ExtensionLifecycleManager(
    ExtensionLoadContext loadContext,
    IServiceScopeFactory scopeFactory,
    IExtensionRepository repository,
    IExtensionNodeStatusRepository nodeStatusRepository,
    IFileSystem fileSystem,
    MagpieHostEnvironment hostEnvironment,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory,
    ILogger<ExtensionLifecycleManager> logger) : IExtensionLifecycleManager
{
    private const string ClaimPrefix = "claim:";

    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(2);

    private static string ResolveAssemblyPath(Assembly assembly)
    {
        return AssemblyLoadContext.GetLoadContext(assembly) is ExtensionLoader.ExtensionAssemblyLoadContext context
            ? context.MainAssemblyPath
            : assembly.Location;
    }

    private static bool Matches(IExtension loaded, Extension extension, IFileSystem fileSystem)
    {
        var assembly = loaded.GetType().Assembly;
        if (!string.IsNullOrWhiteSpace(extension.AssemblyName) &&
            string.Equals(assembly.GetName().Name, extension.AssemblyName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var assemblyPath = ResolveAssemblyPath(assembly);
        if (string.IsNullOrWhiteSpace(extension.FilePath) || string.IsNullOrWhiteSpace(assemblyPath))
        {
            return false;
        }

        return fileSystem.PathsEqual(assemblyPath, extension.FilePath);
    }

    public async Task DispatchStartupHooksAsync(CancellationToken cancellationToken = default)
    {
        if (!loadContext.Loaded.Any(extension => extension is IExtensionLifecycle))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var context = CreateContext(scope.ServiceProvider);
        var rows = (await repository.ListAllAsync(cancellationToken).ConfigureAwait(false)).ToArray();

        foreach (var extension in loadContext.Loaded)
        {
            if (extension is not IExtensionLifecycle lifecycle)
            {
                continue;
            }

            var assembly = extension.GetType().Assembly;
            var runtimeName = assembly.GetName().Name;
            var runtimePath = ResolveAssemblyPath(assembly);
            var row = extension.Id == Guid.Empty
                ? null
                : rows.FirstOrDefault(candidate => candidate.Id == extension.Id);
            if (row is null && !string.IsNullOrWhiteSpace(runtimePath))
            {
                foreach (var candidate in rows.Where(candidate => !string.IsNullOrWhiteSpace(candidate.FilePath)))
                {
                    try
                    {
                        if (fileSystem.PathsEqual(runtimePath, candidate.FilePath!))
                        {
                            row = candidate;
                            break;
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                    {
                    }
                }
            }

            if (row is null && !string.IsNullOrWhiteSpace(runtimeName))
            {
                row = rows.FirstOrDefault(candidate => string.Equals(
                    candidate.PackageId,
                    runtimeName,
                    StringComparison.OrdinalIgnoreCase));
            }

            if (row is null && !string.IsNullOrWhiteSpace(runtimeName))
            {
                row = rows.FirstOrDefault(candidate => string.Equals(
                    candidate.AssemblyName,
                    runtimeName,
                    StringComparison.OrdinalIgnoreCase));
            }

            if (row is null)
            {
                logger.LogWarning(
                    "The lifecycle manager skipped the install and upgrade hooks for extension " +
                    "{ExtensionName} because it has no desired extension row",
                    extension.Name);
            }
            else
            {
                await DispatchVersionHookAsync(lifecycle, context, extension, row, cancellationToken)
                    .ConfigureAwait(false);
            }

            await TryInvokeAsync(
                () => lifecycle.OnAppInitializeAsync(context, cancellationToken),
                extension.Name,
                nameof(IExtensionLifecycle.OnAppInitializeAsync)).ConfigureAwait(false);
        }
    }

    public Task InvokeDisableAsync(Extension extension, CancellationToken cancellationToken = default)
    {
        return InvokeHookAsync(
            extension,
            (lifecycle, context) => lifecycle.OnExtensionDisableAsync(context, cancellationToken),
            nameof(IExtensionLifecycle.OnExtensionDisableAsync));
    }

    public Task InvokeUninstallAsync(Extension extension, CancellationToken cancellationToken = default)
    {
        return InvokeHookAsync(
            extension,
            (lifecycle, context) => lifecycle.OnExtensionUninstallAsync(context, cancellationToken),
            nameof(IExtensionLifecycle.OnExtensionUninstallAsync));
    }

    private ExtensionLifecycleContext CreateContext(IServiceProvider scopedServices)
    {
        return new(hostEnvironment.NodeId, scopedServices, loggerFactory);
    }

    private async Task InvokeHookAsync(
        Extension extension,
        Func<IExtensionLifecycle, ExtensionLifecycleContext, Task> hook,
        string hookName)
    {
        var match = loadContext.Loaded.FirstOrDefault(candidate =>
            candidate is IExtensionLifecycle && Matches(candidate, extension, fileSystem));
        await InvokeLoadedHookAsync(match, hook, hookName).ConfigureAwait(false);
    }

    private async Task InvokeLoadedHookAsync(
        IExtension? match,
        Func<IExtensionLifecycle, ExtensionLifecycleContext, Task> hook,
        string hookName)
    {
        if (match is not IExtensionLifecycle lifecycle)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var context = CreateContext(scope.ServiceProvider);

        try
        {
            await hook(lifecycle, context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Extension {ExtensionName} threw an exception in {HookName}", match.Name, hookName);
        }
    }

    private async Task DispatchVersionHookAsync(
        IExtensionLifecycle lifecycle,
        ExtensionLifecycleContext context,
        IExtension loaded,
        Extension row,
        CancellationToken cancellationToken)
    {
        var currentVersion = loaded.Version.ToString();
        var stored = row.LifecycleVersion;
        var previousVersion = stored;
        string? claimNodeId = null;
        if (stored?.StartsWith(ClaimPrefix, StringComparison.Ordinal) == true)
        {
            var separator = stored.IndexOf(':', ClaimPrefix.Length);
            previousVersion = separator == ClaimPrefix.Length ? null : stored[ClaimPrefix.Length..separator];
            claimNodeId = stored[(separator + 1)..];
        }

        if (claimNodeId is not null &&
            !string.Equals(claimNodeId, hostEnvironment.NodeId, StringComparison.Ordinal) &&
            !await IsNodeGoneAsync(claimNodeId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (string.Equals(previousVersion, currentVersion, StringComparison.Ordinal))
        {
            if (claimNodeId is not null)
            {
                await repository.TrySetLifecycleVersionAsync(row.Id, stored, currentVersion, cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        var claim = $"{ClaimPrefix}{previousVersion}:{hostEnvironment.NodeId}";
        if (!await repository.TrySetLifecycleVersionAsync(row.Id, stored, claim, cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        var succeeded = string.IsNullOrWhiteSpace(previousVersion)
            ? await TryInvokeAsync(
                () => lifecycle.OnExtensionInstallAsync(context, cancellationToken),
                loaded.Name,
                nameof(IExtensionLifecycle.OnExtensionInstallAsync)).ConfigureAwait(false)
            : await TryInvokeAsync(
                Version.TryParse(previousVersion, out var previous)
                    ? () => lifecycle.OnExtensionUpgradeAsync(context, previous, cancellationToken)
                    : () => Task.CompletedTask,
                loaded.Name,
                nameof(IExtensionLifecycle.OnExtensionUpgradeAsync)).ConfigureAwait(false);

        await repository.TrySetLifecycleVersionAsync(
            row.Id,
            claim,
            succeeded ? currentVersion : previousVersion,
            CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<bool> IsNodeGoneAsync(string nodeId, CancellationToken cancellationToken)
    {
        var status = await nodeStatusRepository.GetByNodeIdAsync(nodeId, cancellationToken).ConfigureAwait(false);
        return status is null || status.LastSeenAt < timeProvider.GetUtcNow().UtcDateTime - ClaimTimeout;
    }

    private async Task<bool> TryInvokeAsync(Func<Task> hook, string extensionName, string hookName)
    {
        try
        {
            await hook().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Extension {ExtensionName} threw an exception in {HookName}", extensionName, hookName);
            return false;
        }
    }
}
