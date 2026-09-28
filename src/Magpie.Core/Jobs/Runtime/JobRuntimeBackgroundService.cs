using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Config;
using Magpie.Core.Events;
using Magpie.Core.Observability;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Events;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Runtime;

internal sealed class JobRuntimeBackgroundService(
    IJobRegistry jobRegistry,
    IJobRuntime runtime,
    IJobConcurrencyScheduler concurrencyScheduler,
    JobRuntimeOptions jobRuntimeOptions,
    MagpieHostEnvironment hostEnvironment,
    IServiceScopeFactory scopeFactory,
    IEventBus eventBus,
    JobShutdownSignal shutdownSignal,
    ILogger<JobRuntimeBackgroundService> logger,
    TimeProvider timeProvider,
    DatabaseMigrationState databaseMigration) : BackgroundService
{
    private readonly record struct StuckSchedulerJobsHealResult(
        int HealedCount,
        TimeSpan? NextSchedulerHealCheck);

    private sealed record SchedulerEligibility(bool Eligible, DateTimeOffset CheckedAt);

    private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    private static readonly TimeSpan AdmissionIdlePoll = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AdmissionRebalanceInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DueTriggerPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultNextSchedulerHealCheck = TimeSpan.FromMinutes(30);
    private static readonly DateTime CronIntervalProbeTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan SchedulerEligibilityCacheDuration = TimeSpan.FromSeconds(30);

    private static readonly JobExecutionStatus[] StaleCandidateStatuses =
    [JobExecutionStatus.Running, JobExecutionStatus.CancellationRequested];

    private readonly ConcurrentDictionary<Guid, Task> _inFlight = new();

    private readonly ConcurrentDictionary<Guid, Task> _immediateOutstanding = new();

    private readonly IReadOnlyList<JobSpec> _jobSpecs = jobRegistry.ListJobSpecs();

    private readonly HashSet<string> _jobTypes =
        [.. jobRegistry.ListJobSpecs().Select(spec => spec.JobType)];

    private readonly ConcurrentDictionary<string, byte> _unavailableJobTypes = new(StringComparer.Ordinal);

    private SchedulerEligibility? _schedulerEligibility;

    private static async Task<StuckSchedulerJobsHealResult> HealStuckSchedulerJobsAsync(
        IScheduleTriggerRepository scheduleTriggers,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var enabled = new List<ScheduleTrigger>();
        var pageNumber = 1;
        while (true)
        {
            var page = await scheduleTriggers
                .QueryAsync(
                    new ScheduleTriggerQuery { Enabled = true },
                    new PagingParameter { PageNumber = pageNumber, PageSize = 500 },
                    sort: null,
                    cancellationToken)
                .ConfigureAwait(false);

            enabled.AddRange(page.Items);
            if (enabled.Count >= page.TotalItems || !page.Items.Any())
            {
                break;
            }

            pageNumber++;
        }

        var nextSchedulerHealCheck = GetNextSchedulerHealCheck(enabled);
        var healed = 0;
        foreach (var trigger in enabled)
        {
            if (trigger.NextRunAt is not null)
            {
                continue;
            }

            var referenceTime = GetStuckScheduleReferenceTime(trigger, asOfUtc);
            if (!CronSchedule.TryParse(trigger.Schedule, out var cronSchedule, out _) ||
                cronSchedule.NextOccurrence(referenceTime) is not { } nextRun)
            {
                continue;
            }

            await scheduleTriggers
                .PatchAsync(
                    new ScheduleTriggerPatch { Id = trigger.Id, NextRunAt = new() { Value = nextRun } },
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            healed++;
        }

        return new StuckSchedulerJobsHealResult(healed, nextSchedulerHealCheck);
    }

    private static TimeSpan? GetNextSchedulerHealCheck(IEnumerable<ScheduleTrigger> triggers)
    {
        var shortest = TimeSpan.MaxValue;
        foreach (var trigger in triggers)
        {
            if (!CronSchedule.TryParse(trigger.Schedule, out var cronSchedule, out _))
            {
                continue;
            }

            var first = cronSchedule.NextOccurrence(CronIntervalProbeTime);
            if (first is null)
            {
                continue;
            }

            var second = cronSchedule.NextOccurrence(first.Value.AddSeconds(1));
            if (second is null)
            {
                continue;
            }

            var interval = second.Value - first.Value;
            if (interval > TimeSpan.Zero && interval < shortest)
            {
                shortest = interval;
            }
        }

        return shortest == TimeSpan.MaxValue ? null : shortest;
    }

    private static DateTime GetStuckScheduleReferenceTime(ScheduleTrigger trigger, DateTime fallback)
    {
        if (trigger.LastRunAt is { } lastRunAt)
        {
            return lastRunAt;
        }

        return trigger.CreatedAt == default ? fallback : trigger.CreatedAt;
    }

    private static bool TryGetJobCancellationRequest(
        MagpieEvent magpieEvent,
        out Guid executionId,
        out string ownerNodeId)
    {
        executionId = default;
        ownerNodeId = string.Empty;
        if (magpieEvent is null ||
            !string.Equals(magpieEvent.Name, "job.cancellation.requested", StringComparison.Ordinal) ||
            !magpieEvent.Context.TryGetValue(
                MagpieEvents.Keys.ExecutionId,
                out var rawExecutionId) ||
            !Guid.TryParse(rawExecutionId, out executionId) ||
            !magpieEvent.Context.TryGetValue("execution.ownerNodeId", out var rawOwnerNodeId))
        {
            return false;
        }

        ownerNodeId = rawOwnerNodeId;
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await databaseMigration.Completion.WaitAsync(stoppingToken).ConfigureAwait(false);

        await runtime.InitializeAsync(hostEnvironment.NodeId, stoppingToken).ConfigureAwait(false);

        logger.LogInformation(
            "The job runtime worker started for node {NodeId} (general pool {General} of hard ceiling {Total})",
            hostEnvironment.NodeId,
            concurrencyScheduler.GeneralCapacity,
            concurrencyScheduler.Total);

        await ReclaimOwnOrphanedRunningAsync(stoppingToken).ConfigureAwait(false);

        await Task.WhenAll(
            AdmissionLoopAsync(stoppingToken),
            DueTriggerLoopAsync(stoppingToken),
            SchedulerHealLoopAsync(stoppingToken),
            StaleCleanupLoopAsync(stoppingToken),
            CancellationSubscriptionAsync(stoppingToken)).ConfigureAwait(false);

        await WaitForImmediateOutstandingAsync().ConfigureAwait(false);

        logger.LogInformation("The job runtime worker stopped");
    }

    private async Task SleepAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay > MaxDelay ? MaxDelay : delay, timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task AdmissionLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool admittedThisPass;
            try
            {
                concurrencyScheduler.Rebalance();

                admittedThisPass = await TryAdmitAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The job runtime worker failed to admit work");
                await SleepAsync(ErrorBackoff, stoppingToken).ConfigureAwait(false);
                continue;
            }

            await SleepAsync(admittedThisPass ? AdmissionRebalanceInterval : AdmissionIdlePoll, stoppingToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<bool> IsSchedulerEligibleAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var cached = _schedulerEligibility;
        if (cached is not null && now - cached.CheckedAt < SchedulerEligibilityCacheDuration)
        {
            return cached.Eligible;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using var scopeHandle = scope.ConfigureAwait(false);
        var repository = scope.ServiceProvider.GetRequiredService<IExtensionNodeStatusRepository>();
        var status = await repository.GetByNodeIdAsync(hostEnvironment.NodeId, cancellationToken)
            .ConfigureAwait(false);
        var eligible = status?.SchedulerEligible ?? !hostEnvironment.IsClustered;
        if (cached is null || cached.Eligible != eligible)
        {
            logger.LogInformation(
                "Node {NodeId} scheduler eligibility is now {SchedulerEligible}",
                hostEnvironment.NodeId,
                eligible);
        }

        _schedulerEligibility = new(eligible, now);
        return eligible;
    }

    private bool IsJobTypeRunnableHere(string jobType)
    {
        if (_jobTypes.Contains(jobType))
        {
            return true;
        }

        if (_unavailableJobTypes.TryAdd(jobType, 0))
        {
            logger.LogWarning(
                "No handler is registered for job type {JobType} on node {NodeId}, so its triggers and" +
                " executions are skipped",
                jobType,
                hostEnvironment.NodeId);
        }

        return false;
    }

    internal async Task<bool> TryAdmitAsync(CancellationToken stoppingToken)
    {
        if (!concurrencyScheduler.HasAdmissionCapacity())
        {
            return false;
        }

        if (!await IsSchedulerEligibleAsync(stoppingToken).ConfigureAwait(false))
        {
            return false;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using var scopeHandle = scope.ConfigureAwait(false);
        var executions = scope.ServiceProvider.GetRequiredService<IJobExecutionRepository>();
        const int pageSize = 500;

        var claimable = await executions
            .GetClaimableCandidatesAsync(new() { PageNumber = 1, PageSize = pageSize }, stoppingToken)
            .ConfigureAwait(false);
        var candidates = claimable
            .Where(candidate => IsJobTypeRunnableHere(candidate.JobType))
            .ToList();

        if (candidates.Count == 0)
        {
            return false;
        }

        var admittedAny = false;
        var excluded = new HashSet<Guid>();

        while (concurrencyScheduler.HasAdmissionCapacity() &&
            concurrencyScheduler.TrySelectAdmission(candidates, excluded, out var selected))
        {
            var claimed = await executions
                .TryClaimAsync(selected.Id, hostEnvironment.NodeId, stoppingToken)
                .ConfigureAwait(false);

            if (claimed is null)
            {
                excluded.Add(selected.Id);
                continue;
            }

            JobsTelemetry.AdmissionWaitTime.Record(
                Math.Max(0, (timeProvider.GetUtcNow().UtcDateTime - claimed.CreatedAt).TotalMilliseconds),
                new KeyValuePair<string, object?>("job.type", claimed.JobType));

            concurrencyScheduler.CommitAdmission(selected);
            Dispatch(claimed.Id);
            admittedAny = true;
        }

        return admittedAny;
    }

    private void Dispatch(Guid executionId)
    {
        _inFlight[executionId] = Task.Run(
            async () =>
            {
                await Task.Yield();
                try
                {
                    await runtime.ExecuteAsync(executionId, shutdownSignal.Cancellation.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An unhandled error stopped job execution {ExecutionId}", executionId);
                }
                finally
                {
                    _inFlight.TryRemove(executionId, out _);
                    concurrencyScheduler.Release(executionId);
                }
            },
            default);
    }

    private async Task DueTriggerLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(timeProvider.GetUtcNow().UtcDateTime, stoppingToken).ConfigureAwait(false);
                await SleepAsync(DueTriggerPollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The job scheduler failed to process the due triggers");
                await SleepAsync(ErrorBackoff, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task SchedulerHealLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nextSchedulerHealCheck = await RunSchedulerHealOnceAsync(
                    timeProvider.GetUtcNow().UtcDateTime,
                    stoppingToken)
                    .ConfigureAwait(false);
                await SleepAsync(nextSchedulerHealCheck, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The job scheduler failed to heal the stuck scheduler jobs");
                await SleepAsync(ErrorBackoff, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    internal async Task RunOnceAsync(DateTime asOfUtc, CancellationToken cancellationToken)
    {
        if (!await IsSchedulerEligibleAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using var scopeHandle = scope.ConfigureAwait(false);
        var scheduleTriggers = scope.ServiceProvider.GetRequiredService<IScheduleTriggerRepository>();
        var executions = scope.ServiceProvider.GetRequiredService<IJobExecutionRepository>();
        var jobManager = scope.ServiceProvider.GetRequiredService<IJobManager>();
        var transactionManager = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var systemUsers = await userRepository.QueryAsync(
            new UserQuery { Username = "system" },
            new() { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false);
        var systemUserId = systemUsers.Items.SingleOrDefault()?.Id;
        var triggers = await scheduleTriggers.QueryAllAsync(
            new ScheduleTriggerQuery { DueAsOf = asOfUtc, Enabled = true },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var trigger in triggers)
        {
            await FireTriggerAsync(
                executions,
                jobManager,
                scheduleTriggers,
                transactionManager,
                trigger,
                systemUserId,
                asOfUtc,
                cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task FireTriggerAsync(
        IJobExecutionRepository executions,
        IJobManager jobManager,
        IScheduleTriggerRepository scheduleTriggers,
        ITransactionManager transactionManager,
        ScheduleTrigger trigger,
        Guid? systemUserId,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        if (!IsJobTypeRunnableHere(trigger.JobType))
        {
            return;
        }

        if (trigger.NextRunAt is not { } expectedNextRun)
        {
            return;
        }

        var newNextRun = CronSchedule.TryParse(trigger.Schedule, out var cronSchedule, out _)
            ? cronSchedule.NextOccurrence(asOfUtc)
            : null;
        if (newNextRun is null)
        {
            logger.LogWarning(
                "Schedule trigger {TriggerId} has an invalid or exhausted schedule {Schedule}",
                trigger.Id,
                trigger.Schedule);
            return;
        }

        var correlationId = Guid.NewGuid();
        var lockPartition = jobManager.ResolveLockPartition(
            trigger.JobType,
            trigger.Parameters.ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value, StringComparer.Ordinal));

        var created = await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var claimed = await scheduleTriggers
                    .TryClaimNextRunAsync(trigger.Id, expectedNextRun, newNextRun.Value, ct)
                    .ConfigureAwait(false);
                if (!claimed)
                {
                    return null;
                }

                return await executions.CreateAsync(
                    new()
                    {
                        ScheduleTriggerId = trigger.Id,
                        ExtensionId = trigger.ExtensionId,
                        JobType = trigger.JobType,
                        Scope = trigger.Scope,
                        Parameters = new(trigger.Parameters),
                        ExecutionMode = trigger.ExecutionMode,
                        Status = JobExecutionStatus.Queued,
                        CorrelationId = correlationId,
                        LockPartition = lockPartition,
                    },
                    systemUserId,
                    ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        if (created is null)
        {
            return;
        }

        JobsTelemetry.ScheduleTriggerLag.Record(
            Math.Max(0, (asOfUtc - expectedNextRun).TotalSeconds),
            new KeyValuePair<string, object?>("job.type", trigger.JobType));

        if (trigger.ExecutionMode is not JobExecutionMode.Immediate)
        {
            return;
        }

        DispatchImmediate(created.Id, cancellationToken);
    }

    private void DispatchImmediate(Guid executionId, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _immediateOutstanding[executionId] = completion.Task;
        _ = ExecuteImmediateDetachedAsync(executionId, completion, cancellationToken);
    }

    private Task WaitForShutdownOutstandingAsync()
    {
        var outstanding = shutdownSignal.Outstanding.Values.Select(completion => completion.Task).ToArray();
        return outstanding.Length == 0 ? Task.CompletedTask : Task.WhenAll(outstanding);
    }

    private Task WaitForImmediateOutstandingAsync()
    {
        var outstanding = _immediateOutstanding.Values.ToArray();
        return outstanding.Length == 0 ? Task.CompletedTask : Task.WhenAll(outstanding);
    }

    private async Task ExecuteImmediateDetachedAsync(
        Guid executionId,
        TaskCompletionSource completion,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Yield();
            using var reservation = await concurrencyScheduler
                .AcquireImmediateSlotAsync(cancellationToken)
                .ConfigureAwait(false);

            var scope = scopeFactory.CreateAsyncScope();
            await using var scopeHandle = scope.ConfigureAwait(false);
            var executions = scope.ServiceProvider.GetRequiredService<IJobExecutionRepository>();
            if (await executions
                .TryStartAsync(executionId, hostEnvironment.NodeId, cancellationToken)
                .ConfigureAwait(false))
            {
                await runtime.ExecuteAsync(executionId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Detached immediate job execution {ExecutionId} failed", executionId);
        }
        finally
        {
            _immediateOutstanding.TryRemove(executionId, out _);
            completion.SetResult();
        }
    }

    internal async Task<TimeSpan> RunSchedulerHealOnceAsync(DateTime asOfUtc, CancellationToken cancellationToken)
    {
        if (!await IsSchedulerEligibleAsync(cancellationToken).ConfigureAwait(false))
        {
            return DueTriggerPollInterval;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using var scopeHandle = scope.ConfigureAwait(false);
        var result = await HealStuckSchedulerJobsAsync(
            scope.ServiceProvider.GetRequiredService<IScheduleTriggerRepository>(),
            asOfUtc,
            cancellationToken)
            .ConfigureAwait(false);
        if (result.HealedCount > 0)
        {
            logger.LogInformation(
                "The job scheduler healed {ScheduleTriggerCount} stuck scheduler job(s)",
                result.HealedCount);
        }

        var nextSchedulerHealCheck = result.NextSchedulerHealCheck ?? DefaultNextSchedulerHealCheck;
        return nextSchedulerHealCheck < DefaultNextSchedulerHealCheck
            ? nextSchedulerHealCheck
            : DefaultNextSchedulerHealCheck;
    }

    private async Task StaleCleanupLoopAsync(CancellationToken stoppingToken)
    {
        logger.LogDebug("The stale job cleanup started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The stale job execution cleanup failed");
            }

            var interval = hostEnvironment.IsClustered &&
                jobRuntimeOptions.ClusteredStaleJobCleanupInterval < jobRuntimeOptions.StaleJobCleanupInterval
                ? jobRuntimeOptions.ClusteredStaleJobCleanupInterval
                : jobRuntimeOptions.StaleJobCleanupInterval;
            await SleepAsync(interval, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task RunCleanupOnceAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("The job runtime worker will start the stale job execution cleanup");

        var scope = scopeFactory.CreateAsyncScope();
        await using var scopeHandle = scope.ConfigureAwait(false);
        var jobExecutionRepository = scope.ServiceProvider.GetRequiredService<IJobExecutionRepository>();
        var transactionManager = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        var jobManager = scope.ServiceProvider.GetRequiredService<IJobManager>();
        var retryPolicy = scope.ServiceProvider.GetRequiredService<IJobRetryPolicy>();

        foreach (var status in StaleCandidateStatuses)
        {
            var staleJobs = await jobExecutionRepository.QueryAllAsync(
                query: new()
                {
                    Status = status,
                    LastHeartbeat = new(
                        Min: null,
                        Max: timeProvider.GetUtcNow().UtcDateTime - jobRuntimeOptions.RunningStaleCutoff)
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var job in staleJobs)
            {
                try
                {
                    await TransitionStaleJobAsync(
                        jobExecutionRepository,
                        transactionManager,
                        jobManager,
                        retryPolicy,
                        job,
                        cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "The job runtime worker failed to transition stale job execution {JobExecutionId}",
                        job.Id);
                }
            }
        }
    }

    private async Task TransitionStaleJobAsync(
        IJobExecutionRepository repository,
        ITransactionManager transactionManager,
        IJobManager jobManager,
        IJobRetryPolicy retryPolicy,
        JobExecution job,
        CancellationToken cancellationToken)
    {
        if (job.Status is JobExecutionStatus.Running)
        {
            var reason = $"Node crash detected: last heartbeat at {job.LastHeartbeat:O}";
            await ReclaimAsync(
                repository,
                transactionManager,
                jobManager,
                retryPolicy,
                job,
                reason,
                reclaimKind: "stale_heartbeat")
                .ConfigureAwait(false);
            return;
        }

        if (job.Status is JobExecutionStatus.CancellationRequested)
        {
            logger.LogWarning(
                "The job runtime worker will mark stale CancellationRequested {JobExecutionId} as Cancelled " +
                "(heartbeat {LastHeartbeat})",
                job.Id,
                job.LastHeartbeat);
            await repository
                .TryRecoverInactiveAsync(
                    job,
                    "Cancellation not acknowledged before node crash",
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task CancellationSubscriptionAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("The job cancellation subscriber started for node {NodeId}", hostEnvironment.NodeId);

        try
        {
            using var listener = eventBus is EventBus bus
                ? bus.CreateListener("job.cancellation.requested", localOnly: false, callSite: "cancellation")
                : eventBus.CreateListener("job.cancellation.requested");
            await foreach (var magpieEvent in listener.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!TryGetJobCancellationRequest(magpieEvent, out var executionId, out var ownerNodeId))
                {
                    continue;
                }

                try
                {
                    await DispatchCancellationAsync(executionId, ownerNodeId, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "An unhandled error stopped the job cancellation for execution {ExecutionId}",
                        executionId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        logger.LogInformation("The job cancellation subscriber stopped");
    }

    internal async Task DispatchCancellationAsync(
        Guid executionId,
        string ownerNodeId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(ownerNodeId) &&
            !string.Equals(ownerNodeId, hostEnvironment.NodeId, StringComparison.Ordinal))
        {
            return;
        }

        await runtime.TryCancelAsync(executionId, cancellationToken).ConfigureAwait(false);
    }

    internal async Task ReclaimOwnOrphanedRunningAsync(CancellationToken cancellationToken)
    {
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using var scopeHandle = scope.ConfigureAwait(false);
            var executions = scope.ServiceProvider.GetRequiredService<IJobExecutionRepository>();
            var transactionManager = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
            var jobManager = scope.ServiceProvider.GetRequiredService<IJobManager>();
            var retryPolicy = scope.ServiceProvider.GetRequiredService<IJobRetryPolicy>();

            var orphans = await executions.QueryAllAsync(
                new()
                {
                    NodeId = hostEnvironment.NodeId,
                    Statuses = StaleCandidateStatuses
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (orphans.Count == 0)
            {
                return;
            }

            logger.LogInformation(
                "At startup the job runtime worker will reclaim {OrphanCount} orphaned execution(s) " +
                "owned by node {NodeId}",
                orphans.Count,
                hostEnvironment.NodeId);

            foreach (var orphan in orphans)
            {
                try
                {
                    if (orphan.Status is JobExecutionStatus.CancellationRequested)
                    {
                        await executions.TryRecoverInactiveAsync(
                            orphan,
                            $"Node {hostEnvironment.NodeId} restarted while this execution awaited cancellation",
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    var reason = $"Node {hostEnvironment.NodeId} restarted while this execution was Running";
                    await ReclaimAsync(
                        executions,
                        transactionManager,
                        jobManager,
                        retryPolicy,
                        orphan,
                        reason,
                        reclaimKind: "startup_orphan")
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "At startup the job runtime worker failed to reclaim orphaned Running execution " +
                        "{ExecutionId}",
                        orphan.Id);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The startup self-reclaim of the job runtime worker failed");
        }
    }

    private static async Task<JobFailureTransitionResult> TransitionRunningToFailureAsync(
        IJobExecutionRepository executions,
        ITransactionManager transactionManager,
        IJobManager jobManager,
        IJobRetryPolicy retryPolicy,
        JobExecution failed,
        JobSpec? spec,
        JobFailurePolicy policy,
        string reason)
    {
        var result = new JobFailureTransitionResult(false, false, null);
        await transactionManager.ExecuteWithTransactionAsync(
            async transactionToken =>
            {
                var transitioned = await executions
                    .TryRecoverInactiveAsync(failed, reason, transactionToken)
                    .ConfigureAwait(false);
                if (!transitioned)
                {
                    return;
                }

                if (policy is JobFailurePolicy.Fail ||
                    (policy is JobFailurePolicy.Retry && failed.ExecutionMode is JobExecutionMode.Immediate))
                {
                    result = new(true, PublishFailure: true, Retry: null);
                    return;
                }

                var retry = await retryPolicy.BuildRetryAsync(failed, transactionToken).ConfigureAwait(false);
                if (retry is null)
                {
                    result = new(true, PublishFailure: true, Retry: null);
                    return;
                }

                retry.LockPartition = failed.LockPartition
                    ?? jobManager.ResolveLockPartition(failed.JobType, BuildLockPartitionParameters(failed, spec));
                await executions.CreateAsync(retry, failed.CreatedByUserId, transactionToken).ConfigureAwait(false);
                result = new(true, PublishFailure: false, retry);
            },
            default).ConfigureAwait(false);

        return result;
    }

    private async Task ReclaimAsync(
        IJobExecutionRepository executions,
        ITransactionManager transactionManager,
        IJobManager jobManager,
        IJobRetryPolicy retryPolicy,
        JobExecution orphan,
        string reason,
        string reclaimKind)
    {
        var spec = string.IsNullOrWhiteSpace(orphan.JobType)
            ? null
            : _jobSpecs.FirstOrDefault(candidate =>
                string.Equals(candidate.JobType, orphan.JobType, StringComparison.Ordinal));
        var policy = spec?.FailurePolicy ?? JobFailurePolicy.Fail;
        var result = await TransitionRunningToFailureAsync(
            executions,
            transactionManager,
            jobManager,
            retryPolicy,
            orphan,
            spec,
            policy,
            reason).ConfigureAwait(false);
        if (!result.Transitioned)
        {
            logger.LogDebug(
                "Another cleanup pass already reclaimed orphaned job execution {ExecutionId} ({JobType})",
                orphan.Id,
                orphan.JobType);
            return;
        }

        JobsTelemetry.OrphanReclaimsTotal.Add(
            1,
            new KeyValuePair<string, object?>("job.type", orphan.JobType),
            new KeyValuePair<string, object?>("reclaim_kind", reclaimKind));
        JobsTelemetry.FailuresTotal.Add(
            1,
            new KeyValuePair<string, object?>("job.type", orphan.JobType),
            new KeyValuePair<string, object?>("reason_kind", "orphan_reclaim"));

        logger.LogWarning(
            "The job runtime worker reclaimed orphaned Running job execution {ExecutionId} ({JobType}) " +
            "owned by node {NodeId}: {Reason}",
            orphan.Id,
            orphan.JobType,
            orphan.NodeId,
            reason);

        if (result.Retry is { } retry)
        {
            JobsTelemetry.RetriesTotal.Add(1, new KeyValuePair<string, object?>("job.type", orphan.JobType));
            logger.LogInformation(
                "The job runtime worker queued {Policy} execution {NextExecutionId} for {ExecutionId} " +
                "({JobType}) as attempt {Attempt} at {NotBeforeAt:O}",
                policy,
                retry.Id,
                orphan.Id,
                orphan.JobType,
                retry.Attempt,
                retry.NotBeforeAt);
        }
        else if (result.PublishFailure)
        {
            await eventBus.PublishAsync(
                JobEventBuilder.BuildJobExecutionFailedEvent(orphan.Id, orphan.JobType, reason) with
                {
                    CorrelationId = orphan.CorrelationId
                },
                default).ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await shutdownSignal.Cancellation.CancelAsync().ConfigureAwait(false);

        var inFlight = _inFlight.Values.Where(t => !t.IsCompleted).ToArray();
        var outstanding = WaitForShutdownOutstandingAsync();
        if (inFlight.Length == 0 && outstanding.IsCompleted)
        {
            return;
        }

        logger.LogInformation(
            "The job runtime worker is waiting for {InFlightCount} in-flight execution(s) to stop",
            inFlight.Length);

        try
        {
            await Task.WhenAll(Task.WhenAll(inFlight), outstanding)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static Dictionary<string, string?> BuildLockPartitionParameters(JobExecution execution, JobSpec? spec)
    {
        var parameters = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var parameter in execution.Parameters)
        {
            parameters[parameter.Key] = parameter.Value;
        }

        if (spec is null || spec.LockPartitionEventContextKeys.Count == 0)
        {
            return parameters;
        }

        foreach (var (field, eventContextKey) in spec.LockPartitionEventContextKeys)
        {
            if (parameters.TryGetValue(field, out var existing) && !string.IsNullOrWhiteSpace(existing))
            {
                continue;
            }

            if (execution.TriggerContext.TryGetValue(eventContextKey.ToEventContextKey(), out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                parameters[field] = value;
            }
        }

        return parameters;
    }
}
