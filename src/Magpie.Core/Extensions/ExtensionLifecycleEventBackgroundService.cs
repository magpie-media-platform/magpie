using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Extensions;

internal sealed class ExtensionLifecycleEventBackgroundService(
    IExtensionLifecycleManager lifecycleManager,
    IEventBus eventBus,
    IServiceScopeFactory scopeFactory,
    ILogger<ExtensionLifecycleEventBackgroundService> logger) : BackgroundService
{
    private static bool IsLocalOrigin(MagpieEvent magpieEvent, string nodeId)
    {
        return string.IsNullOrWhiteSpace(magpieEvent.NodeId) ||
            string.Equals(magpieEvent.NodeId, nodeId, StringComparison.Ordinal);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var (isClustered, nodeId) = GetSystemIdentity();
        if (!isClustered)
        {
            return;
        }

        await Task.WhenAll(
            ListenAsync(MagpieEvents.Names.ExtensionDisabled, nodeId, InvokeForeignDisableAsync, stoppingToken),
            ListenAsync(MagpieEvents.Names.ExtensionUninstalled, nodeId, InvokeForeignUninstallAsync, stoppingToken))
            .ConfigureAwait(false);
    }

    private async Task ListenAsync(
        string eventName,
        string nodeId,
        Func<MagpieEvent, CancellationToken, Task> hook,
        CancellationToken stoppingToken)
    {
        var listener = eventBus.CreateListener(eventName, localOnly: false);
        await using var listenerHandle = listener.ConfigureAwait(false);

        try
        {
            await foreach (var magpieEvent in listener.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                if (IsLocalOrigin(magpieEvent, nodeId))
                {
                    continue;
                }

                try
                {
                    await hook(magpieEvent, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(
                        ex,
                        "The lifecycle event service failed to run the cross-node {EventName} hook " +
                        "for event {EventId}",
                        magpieEvent.Name,
                        magpieEvent.Id);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task InvokeForeignDisableAsync(MagpieEvent magpieEvent, CancellationToken cancellationToken)
    {
        if (!magpieEvent.Context.TryGetValue(MagpieEvents.Keys.ExtensionId, out var rawId) ||
            !Guid.TryParse(rawId, out var extensionId))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IExtensionRepository>();
        var extension = await repository.GetByIdAsync(extensionId, cancellationToken).ConfigureAwait(false);
        if (extension is not null)
        {
            await lifecycleManager.InvokeDisableAsync(extension, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task InvokeForeignUninstallAsync(MagpieEvent magpieEvent, CancellationToken cancellationToken)
    {
        magpieEvent.Context.TryGetValue(MagpieEvents.Keys.ExtensionAssemblyName, out var assemblyName);
        magpieEvent.Context.TryGetValue(MagpieEvents.Keys.ExtensionFilePath, out var filePath);
        if (string.IsNullOrWhiteSpace(assemblyName) && string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        magpieEvent.Context.TryGetValue(MagpieEvents.Keys.ExtensionId, out var rawId);
        _ = Guid.TryParse(rawId, out var extensionId);

        var extension = new Extension
        {
            Id = extensionId,
            PackageId = assemblyName ?? string.Empty,
            Name = assemblyName ?? string.Empty,
            Version = string.Empty,
            AssemblyName = assemblyName ?? string.Empty,
            FilePath = filePath ?? string.Empty,
        };
        await lifecycleManager.InvokeUninstallAsync(extension, cancellationToken).ConfigureAwait(false);
    }

    private (bool IsClustered, string NodeId) GetSystemIdentity()
    {
        using var scope = scopeFactory.CreateScope();
        var system = scope.ServiceProvider.GetRequiredService<IMagpieSystem>();
        return (system.IsClustered, system.NodeId);
    }
}
