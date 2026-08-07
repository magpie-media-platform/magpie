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
    ILogger<JobRuntimeBackgroundService> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private static readonly TimeSpan AdmissionIdlePoll = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AdmissionRebalanceInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DueTriggerPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultNextSchedulerHealCheck = TimeSpan.FromMinutes(30);
    private static readonly DateTime CronIntervalProbeTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan UnrunnableQueuedCutoff = TimeSpan.FromHours(1);

    private static readonly JobExecutionStatus[] StaleCandidateStatuses =
    [JobExecutionStatus.Running, JobExecutionStatus.CancellationRequested];

    private readonly ConcurrentDictionary<Guid, Task> _inFlight = new();

    private readonly ConcurrentDictionary<Guid, Task> _immediateOutstanding = new();

    private readonly IReadOnlyList<JobSpec> _jobSpecs = jobRegistry.ListJobSpecs();

    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

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

            trigger.NextRunAt = nextRun;
            await scheduleTriggers.UpdateAsync(trigger, null, cancellationToken).ConfigureAwait(false);
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
        await runtime.InitializeAsync(hostEnvironment.NodeId, stoppingToken).ConfigureAwait(false);
        await ReconcileUnregisteredJobsAsync(stoppingToken).ConfigureAwait(false);

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
            TimedOutJobReapLoopAsync(stoppingToken),
            CancellationSubscriptionAsync(stoppingToken)).ConfigureAwait(false);

        await WaitForImmediateOutstandingAsync().ConfigureAwait(false);

        logger.LogInformation("The job runtime worker stopped");
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
                await TaskHelpers.SleepAsync(ErrorBackoff, _time, stoppingToken).ConfigureAwait(false);
                continue;
            }

            await TaskHelpers
                .SleepAsync(admittedThisPass ? AdmissionRebalanceInterval : AdmissionIdlePoll, _time, stoppingToken)
                .ConfigureAwait(false);
        }
    }

    private async Task ReconcileUnregisteredJobsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var registeredJobTypes = _jobSpecs
                .Select(spec => spec.JobType)
                .ToHashSet(StringComparer.Ordinal);

            var scope = scopeFactory.CreateAsyncScope();
            await using var scopeHandle = scope.ConfigureAwait(false);
            var scheduleTriggers = scope.ServiceProvider.GetRequiredService<IScheduleTriggerRepository>();
            var executions = scope.ServiceProvider.GetRequiredService<IJobExecutionRepository>();
            var transactionManager = scope.ServiceProvider.GetRequiredService<ITransactionManager>();

            const int pageSize = 500;
            var triggers = new List<ScheduleTrigger>();
            var pageNumber = 1;
            while (true)
            {
                var page = await scheduleTriggers.QueryAsync(
                    new(),
                    new() { PageNumber = pageNumber, PageSize = pageSize },
                    null,
                    cancellationToken).ConfigureAwait(false);
                var pageItems = page.Items.ToList();
                triggers.AddRange(pageItems);
                if (pageItems.Count < pageSize)
                {
                    break;
                }

                pageNumber++;
            }

            var orphanedTriggers = triggers
                .Where(trigger => !registeredJobTypes.Contains(trigger.JobType))
                .ToList();

            if (orphanedTriggers.Count == 0)
            {
                return;
            }

            var orphanedJobTypes = orphanedTriggers.Select(trigger => trigger.JobType).Distinct().ToList();

            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var removedTriggers = 0;
                    foreach (var trigger in orphanedTriggers)
                    {
                        if (await scheduleTriggers.DeleteAsync(trigger.Id, null, ct).ConfigureAwait(false))
                        {
                            removedTriggers++;
                        }
                    }

                    var removedExecutions = await executions
                        .DeleteWhereAsync(new JobExecutionQuery { JobTypes = orphanedJobTypes }, ct)
                        .ConfigureAwait(false);

                    logger.LogInformation(
                        "The job runtime worker deleted {OrphanedTriggerCount} trigger(s) and " +
                        "{OrphanedExecutionCount} execution(s) orphaned by unregistered {OrphanedJobTypes}",
                        removedTriggers,
                        removedExecutions,
                        string.Join(", ", orphanedJobTypes));
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The job runtime worker failed to reconcile schedule triggers against " +
                "the registered job catalog");
            throw;
        }
    }

    private async Task<bool> IsSchedulerEligibleAsync(CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using var scopeHandle = scope.ConfigureAwait(false);
        var repository = scope.ServiceProvider.GetRequiredService<IExtensionNodeStatusRepository>();
        var status = await repository.GetByNodeIdAsync(hostEnvironment.NodeId, cancellationToken)
            .ConfigureAwait(false);
        return status?.SchedulerEligible ?? !hostEnvironment.IsClustered;
    }

    internal async Task<bool> TryAdmitAsync(CancellationToken stoppingToken)
    {
        if (!concurrencyScheduler.HasAdmissionCapacity())
        {
            return false;
        }

        if (!await IsSchedulerEligibleAsync(stoppingToken).ConfigureAwait(false))
        {
            logger.LogInformation(
                "Because node {NodeId} is not scheduler eligible, the job runtime worker skipped admission",
                hostEnvironment.NodeId);
            return false;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using var scopeHandle = scope.ConfigureAwait(false);
        var executions = scope.ServiceProvider.GetRequiredService<IJobExecutionRepository>();
        const int pageSize = 500;
        var candidates = new List<JobClaimCandidate>();
        var pageNumber = 1;
        while (true)
        {
            var page = await executions
                .GetClaimableCandidatesAsync(
                    new() { PageNumber = pageNumber, PageSize = pageSize },
                    stoppingToken)
                .ConfigureAwait(false);
            candidates.AddRange(page);
            if (page.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

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
                    await runtime.ExecuteAsync(executionId, _shutdownCts.Token).ConfigureAwait(false);
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
                await RunOnceAsync(_time.GetUtcNow().UtcDateTime, stoppingToken).ConfigureAwait(false);
                await TaskHelpers.SleepAsync(DueTriggerPollInterval, _time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The job scheduler failed to process the due triggers");
                await TaskHelpers.SleepAsync(ErrorBackoff, _time, stoppingToken).ConfigureAwait(false);
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
                    _time.GetUtcNow().UtcDateTime,
                    stoppingToken)
                    .ConfigureAwait(false);
                await TaskHelpers.SleepAsync(nextSchedulerHealCheck, _time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The job scheduler failed to heal the stuck scheduler jobs");
                await TaskHelpers.SleepAsync(ErrorBackoff, _time, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    internal async Task RunOnceAsync(DateTime asOfUtc, CancellationToken cancellationToken)
    {
        if (!await IsSchedulerEligibleAsync(cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation(
                "Because node {NodeId} is not scheduler eligible, the job scheduler skipped the due triggers",
                hostEnvironment.NodeId);
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
        const int pageSize = 500;
        var triggers = new List<ScheduleTrigger>();
        var pageNumber = 1;
        while (true)
        {
            var page = await scheduleTriggers.QueryAsync(
                new ScheduleTriggerQuery { DueAsOf = asOfUtc, Enabled = true },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            triggers.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

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

        if (created is null || trigger.ExecutionMode is not JobExecutionMode.Immediate)
        {
            return;
        }

        await TryDispatchImmediateAsync(executions, created.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TryDispatchImmediateAsync(
        IJobExecutionRepository executions,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        var reservation = await concurrencyScheduler.AcquireImmediateSlotAsync(cancellationToken)
            .ConfigureAwait(false);
        bool started;
        try
        {
            started = await executions
                .TryStartAsync(executionId, hostEnvironment.NodeId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            reservation.Dispose();
            throw;
        }

        if (!started)
        {
            reservation.Dispose();
            return false;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _immediateOutstanding[executionId] = completion.Task;
        _ = ExecuteImmediateDetachedAsync(executionId, reservation, completion, cancellationToken);
        return true;
    }

    private Task WaitForImmediateOutstandingAsync()
    {
        var outstanding = _immediateOutstanding.Values.ToArray();
        return outstanding.Length == 0 ? Task.CompletedTask : Task.WhenAll(outstanding);
    }

    private async Task ExecuteImmediateDetachedAsync(
        Guid executionId,
        IDisposable reservation,
        TaskCompletionSource completion,
        CancellationToken cancellationToken)
    {
        try
        {
            await runtime.ExecuteAsync(executionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Detached immediate job execution {ExecutionId} failed", executionId);
        }
        finally
        {
            reservation.Dispose();
            _immediateOutstanding.TryRemove(executionId, out _);
            completion.SetResult();
        }
    }

    internal async Task<TimeSpan> RunSchedulerHealOnceAsync(DateTime asOfUtc, CancellationToken cancellationToken)
    {
        if (!await IsSchedulerEligibleAsync(cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation(
                "Because node {NodeId} is not scheduler eligible, the job scheduler skipped stuck scheduler job " +
                "healing",
                hostEnvironment.NodeId);
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

            await TaskHelpers
                .SleepAsync(jobRuntimeOptions.StaleJobCleanupInterval, _time, stoppingToken)
                .ConfigureAwait(false);
        }
    }

    private async Task TimedOutJobReapLoopAsync(CancellationToken stoppingToken)
    {
        logger.LogDebug("The timed-out job reaping started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTimedOutJobReapOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The timed-out job execution reaping failed");
            }

            await TaskHelpers
                .SleepAsync(jobRuntimeOptions.TimedOutJobReapInterval, _time, stoppingToken)
                .ConfigureAwait(false);
        }
    }

    internal async Task RunCleanupOnceAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("The job runtime worker will start the stale job execution cleanup");

        var scope = scopeFactory.CreateAsyncScope();
        await using var scopeHandle = scope.ConfigureAwait(false);
        var jobExecutionRepository = scope.ServiceProvider.GetRequiredService<IJobExecutionRepository>();
        var auditRecords = scope.ServiceProvider.GetRequiredService<IAuditRecordRepository>();
        var transactionManager = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        var jobManager = scope.ServiceProvider.GetRequiredService<IJobManager>();
        var retryPolicy = scope.ServiceProvider.GetRequiredService<IJobRetryPolicy>();

        const int pageSize = 500;
        foreach (var status in StaleCandidateStatuses)
        {
            var staleJobs = new List<JobExecution>();
            var pageNumber = 1;
            while (true)
            {
                var page = await jobExecutionRepository.QueryAsync(
                    query: new()
                    {
                        Status = status,
                        LastHeartbeat = new(
                            Min: null,
                            Max: _time.GetUtcNow().UtcDateTime - jobRuntimeOptions.RunningStaleCutoff)
                    },
                    paging: new() { PageNumber = pageNumber, PageSize = pageSize },
                    sort: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                var pageItems = page.Items.ToList();
                staleJobs.AddRange(pageItems);
                if (pageItems.Count < pageSize)
                {
                    break;
                }

                pageNumber++;
            }

            foreach (var job in staleJobs)
            {
                try
                {
                    await TransitionStaleJobAsync(
                        jobExecutionRepository,
                        auditRecords,
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

        await AgeOutUnrunnableQueuedJobsAsync(jobExecutionRepository, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task RunTimedOutJobReapOnceAsync(CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using var scopeHandle = scope.ConfigureAwait(false);
        var jobExecutionRepository = scope.ServiceProvider.GetRequiredService<IJobExecutionRepository>();
        var auditRecords = scope.ServiceProvider.GetRequiredService<IAuditRecordRepository>();
        var transactionManager = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        var jobManager = scope.ServiceProvider.GetRequiredService<IJobManager>();
        var retryPolicy = scope.ServiceProvider.GetRequiredService<IJobRetryPolicy>();

        await ReapTimedOutRunningJobsAsync(
            jobExecutionRepository,
            auditRecords,
            transactionManager,
            jobManager,
            retryPolicy,
            cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ReapTimedOutRunningJobsAsync(
        IJobExecutionRepository repository,
        IAuditRecordRepository auditRecords,
        ITransactionManager transactionManager,
        IJobManager jobManager,
        IJobRetryPolicy retryPolicy,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        const int pageSize = 500;
        var timedOut = new List<JobExecution>();
        var pageNumber = 1;
        while (true)
        {
            var page = await repository.QueryAsync(
                query: new()
                {
                    Status = JobExecutionStatus.Running,
                    TimeoutAt = new(Min: null, Max: now)
                },
                paging: new() { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            timedOut.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        foreach (var job in timedOut)
        {
            try
            {
                var reason = $"Exceeded maximum run time (timed out at {job.TimeoutAt:O})";
                logger.LogWarning(
                    "The job runtime worker will reap timed-out Running job execution {JobExecutionId} ({JobType}). " +
                    "The timeout deadline was {TimeoutAt:O} and the last heartbeat was {LastHeartbeat:O}",
                    job.Id,
                    job.JobType,
                    job.TimeoutAt,
                    job.LastHeartbeat);
                await ReclaimAsync(
                    repository,
                    auditRecords,
                    transactionManager,
                    jobManager,
                    retryPolicy,
                    job,
                    reason,
                    timeoutAudit: true)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "The job runtime worker failed to reap timed-out job execution {JobExecutionId}",
                    job.Id);
            }
        }
    }

    private async Task TransitionStaleJobAsync(
        IJobExecutionRepository repository,
        IAuditRecordRepository auditRecords,
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
                auditRecords,
                transactionManager,
                jobManager,
                retryPolicy,
                job,
                reason,
                timeoutAudit: false)
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
                .TryMarkCancellationRequestedCancelledAsync(
                    job.Id,
                    "Cancellation not acknowledged before node crash",
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task AgeOutUnrunnableQueuedJobsAsync(
        IJobExecutionRepository repository,
        CancellationToken cancellationToken)
    {
        var registeredJobTypes = _jobSpecs
            .Select(spec => spec.JobType)
            .ToHashSet(StringComparer.Ordinal);

        const int pageSize = 500;
        var queuedJobs = new List<JobExecution>();
        var pageNumber = 1;
        while (true)
        {
            var page = await repository.QueryAsync(
                query: new()
                {
                    Status = JobExecutionStatus.Queued,
                    Timestamp = new(
                        Min: null,
                        Max: _time.GetUtcNow().UtcDateTime - UnrunnableQueuedCutoff)
                },
                paging: new() { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            queuedJobs.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        foreach (var job in queuedJobs)
        {
            if (registeredJobTypes.Contains(job.JobType))
            {
                continue;
            }

            try
            {
                var reason = $"No registered handler for job type {job.JobType}";
                logger.LogWarning(
                    "The job runtime worker will mark stale Queued job execution {JobExecutionId} Failed. " +
                    "No handler exists for {JobType}",
                    job.Id,
                    job.JobType);
                var failedByThisRun = await repository
                    .TryMarkQueuedFailedAsync(job.Id, reason, cancellationToken)
                    .ConfigureAwait(false);
                if (failedByThisRun)
                {
                    await eventBus.PublishAsync(
                        JobEventBuilder.BuildJobExecutionFailedEvent(job.Id, job.JobType, reason),
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "The job runtime worker failed to age out un-runnable Queued job execution " +
                    "{JobExecutionId} ({JobType})",
                    job.Id,
                    job.JobType);
            }
        }
    }

    private async Task CancellationSubscriptionAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("The job cancellation subscriber started for node {NodeId}", hostEnvironment.NodeId);

        try
        {
            using var listener = eventBus.CreateListener("job.cancellation.requested");
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
            var auditRecords = scope.ServiceProvider.GetRequiredService<IAuditRecordRepository>();
            var transactionManager = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
            var jobManager = scope.ServiceProvider.GetRequiredService<IJobManager>();
            var retryPolicy = scope.ServiceProvider.GetRequiredService<IJobRetryPolicy>();

            const int pageSize = 500;
            var orphans = new List<JobExecution>();
            var pageNumber = 1;
            while (true)
            {
                var page = await executions.QueryAsync(
                    new()
                    {
                        NodeId = hostEnvironment.NodeId,
                        Status = JobExecutionStatus.Running
                    },
                    new() { PageNumber = pageNumber, PageSize = pageSize },
                    null,
                    cancellationToken).ConfigureAwait(false);
                var pageItems = page.Items.ToList();
                orphans.AddRange(pageItems);
                if (pageItems.Count < pageSize)
                {
                    break;
                }

                pageNumber++;
            }

            if (orphans.Count == 0)
            {
                return;
            }

            logger.LogInformation(
                "At startup the job runtime worker will reclaim {OrphanCount} orphaned Running execution(s) " +
                "owned by node {NodeId}",
                orphans.Count,
                hostEnvironment.NodeId);

            foreach (var orphan in orphans)
            {
                try
                {
                    var reason = $"Node {hostEnvironment.NodeId} restarted while this execution was Running";
                    await ReclaimAsync(
                        executions,
                        auditRecords,
                        transactionManager,
                        jobManager,
                        retryPolicy,
                        orphan,
                        reason,
                        timeoutAudit: false)
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

    private async Task ReclaimAsync(
        IJobExecutionRepository executions,
        IAuditRecordRepository auditRecords,
        ITransactionManager transactionManager,
        IJobManager jobManager,
        IJobRetryPolicy retryPolicy,
        JobExecution orphan,
        string reason,
        bool timeoutAudit)
    {
        var spec = string.IsNullOrWhiteSpace(orphan.JobType)
            ? null
            : _jobSpecs.FirstOrDefault(candidate =>
                string.Equals(candidate.JobType, orphan.JobType, StringComparison.Ordinal));
        var policy = spec?.FailurePolicy ?? JobFailurePolicy.Fail;
        var result = new JobFailureTransitionResult(false, false, null);
        await transactionManager.ExecuteWithTransactionAsync(
            async transactionToken =>
            {
                var transitioned = await executions
                    .TryMarkRunningFailedAsync(orphan.Id, reason, transactionToken)
                    .ConfigureAwait(false);
                if (!transitioned)
                {
                    return;
                }

                if (timeoutAudit)
                {
                    await auditRecords.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Jobs,
                            Operation = "JobTimeout",
                            EntityId = orphan.Id.ToString(),
                            EntityType = nameof(JobExecution),
                            Detail = $"Job execution {orphan.Id} ({orphan.JobType}) timed out. {reason}",
                            UserId = orphan.CreatedByUserId,
                            CorrelationId = orphan.CorrelationId
                        },
                        transactionToken).ConfigureAwait(false);
                }

                if (policy is JobFailurePolicy.Fail ||
                    (policy is JobFailurePolicy.Retry &&
                        orphan.ExecutionMode is JobExecutionMode.Immediate))
                {
                    result = new(true, PublishFailure: true, Retry: null);
                    return;
                }

                var retry = await retryPolicy.BuildRetryAsync(orphan, transactionToken).ConfigureAwait(false);
                if (retry is null)
                {
                    result = new(true, PublishFailure: true, Retry: null);
                    return;
                }

                retry.LockPartition = orphan.LockPartition
                    ?? jobManager.ResolveLockPartition(
                        orphan.JobType,
                        orphan.GetLockPartitionFor(spec));
                await executions.CreateAsync(retry, orphan.CreatedByUserId, transactionToken).ConfigureAwait(false);
                result = new(true, PublishFailure: false, retry);
            },
            default).ConfigureAwait(false);
        if (!result.Transitioned)
        {
            logger.LogDebug(
                "Another cleanup pass already reclaimed orphaned job execution {ExecutionId} ({JobType})",
                orphan.Id,
                orphan.JobType);
            return;
        }

        logger.LogWarning(
            "The job runtime worker reclaimed orphaned Running job execution {ExecutionId} ({JobType}) " +
            "owned by node {NodeId}: {Reason}",
            orphan.Id,
            orphan.JobType,
            orphan.NodeId,
            reason);

        if (result.Retry is { } retry)
        {
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
                JobEventBuilder.BuildJobExecutionFailedEvent(orphan.Id, orphan.JobType, reason),
                default).ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await _shutdownCts.CancelAsync().ConfigureAwait(false);

        var inFlight = _inFlight.Values.Where(t => !t.IsCompleted).ToArray();
        if (inFlight.Length == 0)
        {
            return;
        }

        logger.LogInformation(
            "The job runtime worker sent a cancellation signal to {InFlightCount} in-flight " +
            "execution(s) for shutdown and will wait for them to stop",
            inFlight.Length);

        try
        {
            await Task.WhenAll(inFlight).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override void Dispose()
    {
        _shutdownCts.Dispose();
        base.Dispose();
    }

    private readonly record struct StuckSchedulerJobsHealResult(
        int HealedCount,
        TimeSpan? NextSchedulerHealCheck);
}
