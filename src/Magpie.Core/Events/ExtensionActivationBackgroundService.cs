using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Events;

internal sealed class ExtensionActivationBackgroundService(
    IEventBus eventBus,
    IServiceScopeFactory scopeFactory,
    MagpieHostEnvironment hostEnvironment,
    TimeProvider timeProvider,
    ILogger<ExtensionActivationBackgroundService> logger,
    DatabaseMigrationState databaseMigration) : BackgroundService
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private readonly TaskCompletionSource _listenerReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        await _listenerReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var listener = eventBus is EventBus bus
                ? bus.CreateListener(MagpieEvents.Names.ExtensionActivationChanged, false, "extension-activation")
                : eventBus.CreateListener(MagpieEvents.Names.ExtensionActivationChanged);
            _listenerReady.TrySetResult();
            await databaseMigration.Completion.WaitAsync(stoppingToken).ConfigureAwait(false);
            await Task.WhenAll(
                ListenAsync(listener, stoppingToken),
                RunHeartbeatAsync(stoppingToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _listenerReady.TrySetCanceled(stoppingToken);
        }
        catch (Exception ex)
        {
            _listenerReady.TrySetException(ex);
            throw;
        }
    }

    private async Task ListenAsync(IEventListener listener, CancellationToken cancellationToken)
    {
        await foreach (var magpieEvent in listener.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (magpieEvent.Context.TryGetValue(MagpieEvents.Keys.ExtensionGeneration, out var raw) &&
                long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var generation))
            {
                await RefreshNodeStatusAsync(generation, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), timeProvider);
        do
        {
            await RefreshNodeStatusAsync(null, cancellationToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task RefreshNodeStatusAsync(long? generation, CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using var scopeHandle = scope.ConfigureAwait(false);
            var repository = scope.ServiceProvider.GetRequiredService<IExtensionNodeStatusRepository>();
            var currentGeneration = generation ?? await scope.ServiceProvider
                .GetRequiredService<IExtensionActivationGenerationRepository>()
                .GetCurrentAsync(cancellationToken).ConfigureAwait(false);
            var existing = await repository.GetByNodeIdAsync(hostEnvironment.NodeId, cancellationToken)
                .ConfigureAwait(false);
            var health = existing?.ExtensionHealth ?? ExtensionNodeHealth.Offline;
            if (health == ExtensionNodeHealth.CurrentHealthy && existing!.AppliedGeneration < currentGeneration)
            {
                health = ExtensionNodeHealth.StaleRequiresRestart;
            }

            await repository.UpsertAsync(
                new ExtensionNodeStatus
                {
                    NodeId = hostEnvironment.NodeId,
                    AppliedGeneration = existing?.AppliedGeneration ?? 0,
                    ExtensionHealth = health,
                    SchedulerEligible = !hostEnvironment.IsClustered || health == ExtensionNodeHealth.CurrentHealthy,
                    LastSeenAt = timeProvider.GetUtcNow().UtcDateTime,
                    Message = health switch
                    {
                        ExtensionNodeHealth.CurrentHealthy => "Extension activation is current and healthy.",
                        ExtensionNodeHealth.StaleRequiresRestart => "Extension changes require a restart.",
                        ExtensionNodeHealth.Offline => "Extension activation status is unavailable.",
                        ExtensionNodeHealth.ActivationFailed => existing?.Message ?? "Extension activation failed.",
                        _ => existing?.Message,
                    },
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The heartbeat for extension nodes failed");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public override void Dispose()
    {
        _refreshGate.Dispose();
        base.Dispose();
    }
}
