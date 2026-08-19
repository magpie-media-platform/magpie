using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Events;

internal sealed class ClusterBackplaneBackgroundService(
    IEventBus eventBus,
    MagpieHostEnvironment hostEnvironment,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ClusterBackplaneBackgroundService> logger,
    IClusterBackplane? backplane = null) : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(15);

    private static string? BuildHeartbeatMessage(string? currentMessage, ExtensionNodeHealth health)
    {
        return health switch
        {
            ExtensionNodeHealth.CurrentHealthy => "Extension activation is current and healthy.",
            ExtensionNodeHealth.StaleRequiresRestart => "Extension changes require a restart.",
            ExtensionNodeHealth.Offline => "Extension activation status is unavailable.",
            ExtensionNodeHealth.ActivationFailed => currentMessage ?? "Extension activation failed.",
            _ => currentMessage,
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(RunCrossNodeAsync(stoppingToken), RunHeartbeatAsync(stoppingToken)).ConfigureAwait(false);
    }

    private async Task RunCrossNodeAsync(CancellationToken cancellationToken)
    {
        if (backplane is null)
        {
            logger.LogInformation("No cluster backplane is registered. Cross-node event distribution will not run");
            return;
        }

        await Task.WhenAll(
            BroadcastLocalEventsAsync(cancellationToken),
            ReceivePeerEventsAsync(cancellationToken)).ConfigureAwait(false);
    }

    private async Task BroadcastLocalEventsAsync(CancellationToken cancellationToken)
    {
        using var listener = eventBus is EventBus bus
            ? bus.CreateListener(eventName: null, localOnly: false, callSite: "backplane")
            : eventBus.CreateListener();
        try
        {
            await foreach (var magpieEvent in listener.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await RelayOutboundAsync(magpieEvent, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "The cluster backplane failed to broadcast event {EventName} ({EventId}) to peer nodes",
                        magpieEvent.Name,
                        magpieEvent.Id);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ReceivePeerEventsAsync(CancellationToken cancellationToken)
    {
        using var listener = backplane!.CreateListener();
        try
        {
            await foreach (var magpieEvent in listener.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await RelayInboundAsync(magpieEvent, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The cluster backplane worker failed. Cross-node event distribution stopped");
        }
    }

    internal Task RelayOutboundAsync(MagpieEvent magpieEvent, CancellationToken cancellationToken)
    {
        if (backplane is null || !string.Equals(magpieEvent.NodeId, hostEnvironment.NodeId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return backplane.BroadcastEventAsync(magpieEvent, cancellationToken);
    }

    internal Task RelayInboundAsync(MagpieEvent magpieEvent, CancellationToken cancellationToken)
    {
        if (string.Equals(magpieEvent.NodeId, hostEnvironment.NodeId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return eventBus.PublishAsync(magpieEvent, cancellationToken);
    }

    private async Task RunHeartbeatAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("The heartbeat service for extension nodes started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var scope = scopeFactory.CreateAsyncScope();
                await using var scopeHandle = scope.ConfigureAwait(false);
                var nodeStatusRepository = scope.ServiceProvider.GetRequiredService<IExtensionNodeStatusRepository>();
                var activationGenerationRepository = scope.ServiceProvider
                    .GetRequiredService<IExtensionActivationGenerationRepository>();
                var existing = await nodeStatusRepository
                    .GetByNodeIdAsync(hostEnvironment.NodeId, stoppingToken)
                    .ConfigureAwait(false);
                var currentGeneration = await activationGenerationRepository.GetCurrentAsync(stoppingToken)
                    .ConfigureAwait(false);
                var health = existing is null
                    ? ExtensionNodeHealth.Offline
                    : existing.ExtensionHealth is ExtensionNodeHealth.CurrentHealthy &&
                        existing.AppliedGeneration < currentGeneration
                            ? ExtensionNodeHealth.StaleRequiresRestart
                            : existing.ExtensionHealth;
                await nodeStatusRepository.UpsertAsync(
                    new ExtensionNodeStatus
                    {
                        NodeId = hostEnvironment.NodeId,
                        AppliedGeneration = existing?.AppliedGeneration ?? 0,
                        ExtensionHealth = health,
                        SchedulerEligible = !hostEnvironment.IsClustered ||
                            health is ExtensionNodeHealth.CurrentHealthy,
                        LastSeenAt = timeProvider.GetUtcNow().UtcDateTime,
                        Message = BuildHeartbeatMessage(existing?.Message, health),
                    },
                    stoppingToken).ConfigureAwait(false);
                await TaskHelpers.SleepAsync(HeartbeatInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The heartbeat for extension nodes failed. The heartbeat service will retry");
                await TaskHelpers.SleepAsync(ErrorBackoff, stoppingToken).ConfigureAwait(false);
            }
        }

        logger.LogInformation("The heartbeat service for extension nodes stopped");
    }
}
