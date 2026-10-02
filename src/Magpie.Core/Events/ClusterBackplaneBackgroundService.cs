using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Events;

internal sealed class ClusterBackplaneBackgroundService(
    IEventBus eventBus,
    MagpieHostEnvironment hostEnvironment,
    ILogger<ClusterBackplaneBackgroundService> logger,
    DatabaseMigrationState databaseMigration,
    IClusterBackplane? backplane = null) : BackgroundService
{
    private readonly TaskCompletionSource _listenerReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        await _listenerReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return RunCrossNodeAsync(stoppingToken);
    }

    private async Task RunCrossNodeAsync(CancellationToken cancellationToken)
    {
        if (backplane is null || !hostEnvironment.IsClustered)
        {
            _listenerReady.TrySetResult();
            logger.LogInformation(
                "Cross-node event distribution is disabled: backplane registered {BackplaneRegistered}, " +
                "clustered {Clustered}",
                backplane is not null,
                hostEnvironment.IsClustered);
            return;
        }

        await Task.WhenAll(
            BroadcastLocalEventsAsync(cancellationToken),
            ReceivePeerEventsAsync(cancellationToken)).ConfigureAwait(false);
    }

    private async Task BroadcastLocalEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var listener = eventBus.CreateListener(eventName: null, localOnly: false, callSite: "backplane");
            _listenerReady.TrySetResult();
            await databaseMigration.Completion.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
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
            _listenerReady.TrySetCanceled(cancellationToken);
        }
        catch (Exception ex)
        {
            _listenerReady.TrySetException(ex);
            throw;
        }
    }

    private async Task ReceivePeerEventsAsync(CancellationToken cancellationToken)
    {
        using var listener = backplane!.CreateListener();
        try
        {
            await databaseMigration.Completion.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
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
            logger.LogError(ex, "The cluster backplane worker failed and stopped cross-node event distribution");
        }
    }

    private Task RelayOutboundAsync(MagpieEvent magpieEvent, CancellationToken cancellationToken)
    {
        if (backplane is null || !string.Equals(magpieEvent.NodeId, hostEnvironment.NodeId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return backplane.BroadcastEventAsync(magpieEvent, cancellationToken);
    }

    private Task RelayInboundAsync(MagpieEvent magpieEvent, CancellationToken cancellationToken)
    {
        if (string.Equals(magpieEvent.NodeId, hostEnvironment.NodeId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return eventBus.PublishAsync(magpieEvent, cancellationToken);
    }
}
