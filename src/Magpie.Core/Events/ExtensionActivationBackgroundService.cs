using System;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Magpie.Abstractions.Database;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Events;

internal sealed class ExtensionActivationBackgroundService(
    IEventBus eventBus,
    IServiceScopeFactory scopeFactory,
    MagpieHostEnvironment hostEnvironment,
    HostStatusInfo hostStatus,
    TimeProvider timeProvider,
    ILogger<ExtensionActivationBackgroundService> logger,
    DatabaseMigrationState databaseMigration,
    ExtensionActivationBackgroundService.DatabaseFailureInterceptor databaseFailures) : BackgroundService
{
    internal sealed class DatabaseFailureInterceptor : DbCommandInterceptor, IDbConnectionInterceptor
    {
        private readonly Channel<bool> _failures = Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

        public ChannelReader<bool> Failures => _failures.Reader;

        public void RequestRecheck()
        {
            _failures.Writer.TryWrite(true);
        }

        public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
        {
            _failures.Writer.TryWrite(true);
        }

        public override Task CommandFailedAsync(
            DbCommand command,
            CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            _failures.Writer.TryWrite(true);
            return Task.CompletedTask;
        }

        public void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData)
        {
            _failures.Writer.TryWrite(true);
        }

        public Task ConnectionFailedAsync(
            DbConnection connection,
            ConnectionErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            _failures.Writer.TryWrite(true);
            return Task.CompletedTask;
        }
    }

    [SuppressMessage("Ordering", "SA1201", Justification = "Nested types come first in this codebase.")]
    private static readonly TimeSpan FailureRecheckInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMinutes(2);

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
            using var listener = eventBus.CreateListener(
                MagpieEvents.Names.ExtensionActivationChanged,
                false,
                "extension-activation");
            using var migrationListener = eventBus.CreateListener(
                MagpieEvents.Names.DatabaseMigrated,
                false,
                "extension-activation");
            _listenerReady.TrySetResult();
            await databaseMigration.Completion.WaitAsync(stoppingToken).ConfigureAwait(false);
            await Task.WhenAll(
                ListenAsync(listener, stoppingToken),
                RecheckAfterMigrationsAsync(migrationListener, stoppingToken),
                RunHeartbeatAsync(stoppingToken),
                RecheckAfterDatabaseFailuresAsync(stoppingToken)).ConfigureAwait(false);
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

    private async Task RecheckAfterMigrationsAsync(IEventListener listener, CancellationToken cancellationToken)
    {
        await foreach (var _ in listener.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            databaseFailures.RequestRecheck();
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

    private async Task RecheckAfterDatabaseFailuresAsync(CancellationToken cancellationToken)
    {
        await foreach (var _ in databaseFailures.Failures.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await RefreshNodeStatusAsync(null, cancellationToken).ConfigureAwait(false);
            await Task.Delay(FailureRecheckInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task RefreshNodeStatusAsync(long? generation, CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using var scopeHandle = scope.ConfigureAwait(false);
            var unknownMigrations = await scope.ServiceProvider.GetRequiredService<IDatabaseManagementProvider>()
                .GetUnknownMigrationsAsync(cancellationToken).ConfigureAwait(false);
            if (unknownMigrations.Count > 0 && databaseMigration.UnknownMigrations.Count == 0)
            {
                logger.LogError(
                    "The database schema is newer than node {NodeId}. Unknown migrations: {Migrations}",
                    hostEnvironment.NodeId,
                    string.Join(", ", unknownMigrations));
            }

            databaseMigration.RecordSchemaCheck(unknownMigrations);
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

            // A restart request exits with success, and systemd restarts a unit only on failure.
            var canRestart = hostStatus.Mode is HostMode.Sentinel or HostMode.WindowsService;

            await repository.UpsertAsync(
                new ExtensionNodeStatus
                {
                    NodeId = hostEnvironment.NodeId,
                    AppliedGeneration = existing?.AppliedGeneration ?? 0,
                    ExtensionHealth = health,
                    SchedulerEligible = unknownMigrations.Count == 0 &&
                        (!hostEnvironment.IsClustered || health == ExtensionNodeHealth.CurrentHealthy),
                    LastSeenAt = timeProvider.GetUtcNow().UtcDateTime,
                    Message = unknownMigrations.Count > 0
                        ? "The database schema is newer than this node. Unknown migrations: " +
                            $"{string.Join(", ", unknownMigrations)}."
                        : health switch
                        {
                            ExtensionNodeHealth.CurrentHealthy => "Extension activation is current and healthy.",
                            ExtensionNodeHealth.StaleRequiresRestart when hostEnvironment.IsClustered && !canRestart =>
                                "Extension changes require a restart. Restart this node manually.",
                            ExtensionNodeHealth.StaleRequiresRestart => "Extension changes require a restart.",
                            ExtensionNodeHealth.Offline => "Extension activation status is unavailable.",
                            ExtensionNodeHealth.ActivationFailed => existing?.Message ?? "Extension activation failed.",
                            _ => existing?.Message,
                        },
                },
                cancellationToken).ConfigureAwait(false);

            if (health == ExtensionNodeHealth.StaleRequiresRestart &&
                hostEnvironment.IsClustered &&
                canRestart &&
                unknownMigrations.Count == 0 &&
                !hostStatus.IsShutdownRequested &&
                await IsRestartTurnAsync(repository, currentGeneration, cancellationToken).ConfigureAwait(false))
            {
                logger.LogInformation(
                    "Node {NodeId} restarts to apply extension activation generation {Generation}",
                    hostEnvironment.NodeId,
                    currentGeneration);
                await scope.ServiceProvider.GetRequiredService<IMagpieSystem>()
                    .RequestRestartAsync(null, cancellationToken).ConfigureAwait(false);
            }
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

    private async Task<bool> IsRestartTurnAsync(
        IExtensionNodeStatusRepository repository,
        long generation,
        CancellationToken cancellationToken)
    {
        var seenAfter = timeProvider.GetUtcNow().UtcDateTime - HeartbeatTimeout;
        var statuses = await repository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return statuses.All(status =>
            string.CompareOrdinal(status.NodeId, hostEnvironment.NodeId) >= 0 ||
            status.AppliedGeneration >= generation ||
            status.LastSeenAt < seenAfter);
    }

    public override void Dispose()
    {
        _refreshGate.Dispose();
        base.Dispose();
    }
}
