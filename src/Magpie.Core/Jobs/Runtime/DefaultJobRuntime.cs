using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Core.Observability;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Runtime;

internal sealed class DefaultJobRuntime : IJobRuntime
{
    private sealed class RunningExecution(CancellationTokenSource cts)
    {
        public CancellationTokenSource Cts { get; } = cts;
        public bool OperatorCancelled { get; set; }
        public bool LeaseLost { get; set; }
    }

    private const string OutcomeCompleted = "completed";

    private const string OutcomeFailed = "failed";

    private const string OutcomeCancelled = "cancelled";

    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(30);

    private readonly IJobExecutionScopeFactory _jobExecutionScopeFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobRegistry _registry;
    private readonly IEventBus _eventBus;
    private readonly ILogger<DefaultJobRuntime> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _heartbeatInterval;
    private readonly ConcurrentDictionary<Guid, RunningExecution> _running = new();
    private readonly JobShutdownSignal _shutdownSignal;
    private string _nodeId;

    private volatile bool _disposing;

    public DefaultJobRuntime(
        IJobExecutionScopeFactory jobExecutionScopeFactory,
        IServiceScopeFactory scopeFactory,
        IJobRegistry registry,
        MagpieHostEnvironment hostEnvironment,
        IEventBus eventBus,
        JobShutdownSignal shutdownSignal,
        TimeProvider timeProvider,
        ILogger<DefaultJobRuntime> logger)
        : this(
            jobExecutionScopeFactory,
            scopeFactory,
            registry,
            hostEnvironment,
            eventBus,
            shutdownSignal,
            timeProvider,
            logger,
            DefaultHeartbeatInterval)
    {
    }

    internal DefaultJobRuntime(
        IJobExecutionScopeFactory jobScopes,
        IServiceScopeFactory scopeFactory,
        IJobRegistry registry,
        MagpieHostEnvironment hostEnvironment,
        IEventBus eventBus,
        JobShutdownSignal shutdownSignal,
        TimeProvider timeProvider,
        ILogger<DefaultJobRuntime> logger,
        TimeSpan heartbeatInterval)
    {
        _jobExecutionScopeFactory = jobScopes;
        _scopeFactory = scopeFactory;
        _registry = registry;
        _eventBus = eventBus;
        _shutdownSignal = shutdownSignal;
        _timeProvider = timeProvider;
        _logger = logger;
        _heartbeatInterval = heartbeatInterval;
        _nodeId = hostEnvironment.NodeId;
    }

    public string Name => "in-process";

    public Task InitializeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            _nodeId = nodeId;
        }

        _logger.LogInformation("Job runtime {RuntimeName} is ready for node {NodeId}", Name, _nodeId);
        return Task.CompletedTask;
    }

    public async Task ExecuteAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        var repos = _scopeFactory.CreateAsyncScope();
        await using var reposHandle = repos.ConfigureAwait(false);
        var executions = repos.ServiceProvider.GetRequiredService<IJobExecutionRepository>();
        var transactionManager = repos.ServiceProvider.GetRequiredService<ITransactionManager>();
        var jobManager = repos.ServiceProvider.GetRequiredService<IJobManager>();
        var retryPolicy = repos.ServiceProvider.GetRequiredService<IJobRetryPolicy>();

        var execution = await executions.GetByIdAsync(executionId, default).ConfigureAwait(false);
        if (execution is null)
        {
            _logger.LogWarning("Job execution {ExecutionId} disappeared before it ran", executionId);
            return;
        }

        var alreadyStartedByThisNode = execution.Status is JobExecutionStatus.Running &&
            string.Equals(execution.NodeId, _nodeId, StringComparison.Ordinal);
        if (execution.Status is not JobExecutionStatus.Queued && !alreadyStartedByThisNode)
        {
            return;
        }

        if (!_registry.TryGetJobSpec(execution.JobType, out var spec))
        {
            _logger.LogError(
                "No job handler exists for job type {JobType}, so the runtime will mark execution {ExecutionId}" +
                " as failed",
                execution.JobType,
                executionId);
            JobsTelemetry.FailuresTotal.Add(
                1,
                new KeyValuePair<string, object?>("job.type", execution.JobType),
                new KeyValuePair<string, object?>("reason_kind", "no_handler"));
            var noHandlerReason = $"No handler registered for job type {execution.JobType}";
            var failedByThisRun = execution.Status is JobExecutionStatus.Running
                ? await executions.TryMarkRunningFailedAsync(executionId, noHandlerReason, default)
                    .ConfigureAwait(false)
                : await executions.TryMarkQueuedFailedAsync(executionId, noHandlerReason, default)
                    .ConfigureAwait(false);
            if (failedByThisRun)
            {
                await _eventBus.PublishAsync(
                    JobEventBuilder.BuildJobExecutionFailedEvent(execution.Id, execution.JobType, noHandlerReason) with
                    {
                        CorrelationId = execution.CorrelationId
                    },
                    default).ConfigureAwait(false);
            }

            return;
        }

        if (!alreadyStartedByThisNode)
        {
            var claimed = await executions.TryStartAsync(executionId, _nodeId, default)
                .ConfigureAwait(false);
            if (!claimed)
            {
                _logger.LogDebug(
                    "This node failed to claim job execution {ExecutionId} because another node took it, or " +
                    "another execution holds the same lock partition",
                    executionId);
                return;
            }
        }

        var failurePolicy = spec.FailurePolicy;

        var jobCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdownSignal.Cancellation.Token);
        var runningEntry = new RunningExecution(jobCts);
        _running[executionId] = runningEntry;
        _shutdownSignal.Outstanding[executionId] =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        JobExecution? registeredState;
        try
        {
            registeredState = await executions.GetByIdAsync(executionId, default).ConfigureAwait(false);
        }
        catch
        {
            _running.TryRemove(executionId, out _);
            CompleteShutdownTracking(executionId);
            jobCts.Dispose();
            throw;
        }

        if (registeredState?.Status is JobExecutionStatus.CancellationRequested)
        {
            runningEntry.OperatorCancelled = true;
            await jobCts.CancelAsync().ConfigureAwait(false);
        }
        else if (registeredState?.Status is not JobExecutionStatus.Running)
        {
            _running.TryRemove(executionId, out _);
            CompleteShutdownTracking(executionId);
            jobCts.Dispose();
            return;
        }

        DistributedContextPropagator.Current.ExtractTraceIdAndState(
            execution.TriggerContext,
            static (object? carrier, string key, out string? value, out IEnumerable<string>? values) =>
            {
                values = null;
                value = ((IDictionary<string, string>)carrier!).TryGetValue(key, out var found) ? found : null;
            },
            out var traceParent,
            out var traceState);
        var links = ActivityContext.TryParse(traceParent, traceState, isRemote: true, out var triggerContext)
            ? new[] { new ActivityLink(triggerContext) }
            : null;
        using var activity = JobsTelemetry.Source.StartActivity(
            $"job {execution.JobType}",
            ActivityKind.Internal,
            parentContext: default,
            tags: null,
            links: links);
        if (activity is not null)
        {
            activity.SetTag("job.execution_id", executionId);
            activity.SetTag("job.type", execution.JobType);
            activity.SetTag("job.trigger_mode", GetTriggerMode(execution));
            activity.SetTag("job.execution_mode", execution.ExecutionMode.ToString());
            activity.SetTag("job.attempt", execution.Attempt ?? 1);
            activity.SetTag("job.lock_partition_kind", spec.ConcurrencyScope.ToString());
            activity.SetTag("job.correlation_id", execution.CorrelationId);
            activity.SetTag("job.parent_execution_id", execution.ParentExecutionId);
            activity.SetTag("job.node_id", _nodeId);
        }

        var startedTimestamp = Stopwatch.GetTimestamp();
        var outcome = OutcomeCompleted;

        using var heartbeatCts = new CancellationTokenSource();
        var heartbeatPump = RunHeartbeatPumpAsync(executionId, heartbeatCts.Token);

        try
        {
            var jobExecutionScope = _jobExecutionScopeFactory.CreateScope(executionId);

            await using var _ = jobExecutionScope.ConfigureAwait(false);
            jobExecutionScope.SetParentContext(executionId, execution.CorrelationId);

            if (!jobExecutionScope.TryGetHandler(spec, out var handler))
            {
                _logger.LogError(
                    "No job handler exists for job type {JobType}, so the runtime will mark execution " +
                    "{ExecutionId} as failed",
                    execution.JobType,
                    executionId);
                JobsTelemetry.FailuresTotal.Add(
                    1,
                    new KeyValuePair<string, object?>("job.type", execution.JobType),
                    new KeyValuePair<string, object?>("reason_kind", "no_handler"));
                outcome = OutcomeFailed;
                var noHandlerReason = $"No handler registered for job type {execution.JobType}";
                activity?.SetStatus(ActivityStatusCode.Error, noHandlerReason);
                var failedByThisRun = await executions
                    .TryMarkRunningFailedAsync(executionId, noHandlerReason, default)
                    .ConfigureAwait(false);
                if (failedByThisRun)
                {
                    await _eventBus.PublishAsync(
                        JobEventBuilder.BuildJobExecutionFailedEvent(
                            execution.Id,
                            execution.JobType,
                            noHandlerReason) with
                        {
                            CorrelationId = execution.CorrelationId
                        },
                        default).ConfigureAwait(false);
                }

                return;
            }

            var jobContext = new JobContext(
                ExecutionId: executionId,
                ScheduleTriggerId: execution.ScheduleTriggerId,
                EventTriggerId: execution.EventTriggerId,
                ManualTriggerUserId: execution.CreatedByUserId,
                ExecutionMode: execution.ExecutionMode,
                Parameters: execution.Parameters,
                Event: execution.EventTriggerId is null
                    ? null
                    : execution.TriggerContext.ToMagpieEvent(),
                CorrelationId: execution.CorrelationId);

            await handler.ExecuteAsync(jobContext, jobCts.Token).ConfigureAwait(false);
            await jobExecutionScope.FlushProgressAsync(jobCts.Token).ConfigureAwait(false);
            var completedByThisRun = await executions
                .TryMarkRunningCompletedAsync(executionId, default)
                .ConfigureAwait(false);
            if (!completedByThisRun)
            {
                var cancelledByThisRun = await executions
                    .TryMarkCancellationRequestedCancelledAsync(executionId, reason: null, default)
                    .ConfigureAwait(false);
                if (cancelledByThisRun)
                {
                    outcome = OutcomeCancelled;
                    activity?.AddEvent(new ActivityEvent("cancelled"));
                    _logger.LogInformation(
                        "An operator cancelled job execution {ExecutionId} ({JobType})",
                        executionId,
                        execution.JobType);
                }
            }

            activity?.SetStatus(ActivityStatusCode.Ok);

            _logger.LogDebug(
                "The job handler returned for execution {ExecutionId} ({JobType})",
                executionId,
                execution.JobType);
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested)
        {
            var hostShutdown = cancellationToken.IsCancellationRequested ||
                _shutdownSignal.Cancellation.Token.IsCancellationRequested ||
                _disposing;
            if (runningEntry.OperatorCancelled)
            {
                outcome = OutcomeCancelled;
                activity?.AddEvent(new ActivityEvent("cancelled"));
                activity?.SetStatus(ActivityStatusCode.Ok);
                var cancelledByThisRun = await executions
                    .TryMarkCancellationRequestedCancelledAsync(executionId, reason: null, default)
                    .ConfigureAwait(false);
                if (cancelledByThisRun)
                {
                    _logger.LogInformation(
                        "An operator cancelled job execution {ExecutionId} ({JobType})",
                        executionId,
                        execution.JobType);
                }
            }
            else if (runningEntry.LeaseLost)
            {
                // Another node owns the execution now, so this run writes nothing for it.
                const string reason = "Lost the execution lease to another node";
                outcome = OutcomeFailed;
                activity?.SetStatus(ActivityStatusCode.Error, reason);
                _logger.LogWarning(
                    "Job execution {ExecutionId} ({JobType}) stopped because node {NodeId} lost its lease",
                    executionId,
                    execution.JobType,
                    _nodeId);
            }
            else if (hostShutdown)
            {
                const string reason = "Interrupted by host shutdown";
                outcome = OutcomeFailed;
                activity?.SetStatus(ActivityStatusCode.Error, reason);
                var failedByThisRun = await TransitionFailureAsync(
                    executions,
                    transactionManager,
                    jobManager,
                    retryPolicy,
                    execution,
                    spec,
                    failurePolicy,
                    reason)
                    .ConfigureAwait(false);
                if (failedByThisRun)
                {
                    JobsTelemetry.FailuresTotal.Add(
                        1,
                        new KeyValuePair<string, object?>("job.type", execution.JobType),
                        new KeyValuePair<string, object?>("reason_kind", "host_shutdown"));
                    _logger.LogWarning(
                        "The host shutdown interrupted execution {ExecutionId} ({JobType}), so the runtime " +
                        "applied the {Policy} policy",
                        executionId,
                        execution.JobType,
                        failurePolicy);
                }
                else
                {
                    var cancelledByThisRun = await executions
                        .TryMarkCancellationRequestedCancelledAsync(executionId, reason: null, default)
                        .ConfigureAwait(false);
                    if (cancelledByThisRun)
                    {
                        outcome = OutcomeCancelled;
                        activity?.AddEvent(new ActivityEvent("cancelled"));
                        activity?.SetStatus(ActivityStatusCode.Ok);
                        _logger.LogInformation(
                            "An operator cancelled job execution {ExecutionId} ({JobType})",
                            executionId,
                            execution.JobType);
                    }
                }
            }
        }
        catch (UpstreamThrottledException ex)
        {
            var now = _timeProvider.GetUtcNow();
            var retryAt = ex.RetryAt > now ? ex.RetryAt : now.AddSeconds(1);
            var spread = executionId.ToByteArray()[0] % 30;
            var notBefore = (retryAt < DateTimeOffset.MaxValue.AddSeconds(-spread)
                ? retryAt.AddSeconds(spread) : DateTimeOffset.MaxValue).UtcDateTime;
            var deferred = await executions.TryDeferRunningAsync(executionId, _nodeId, notBefore, default)
                .ConfigureAwait(false);
            if (deferred)
            {
                outcome = "deferred";
                activity?.SetTag("job.not_before", notBefore);
                _logger.LogInformation("Upstream throttling deferred execution {ExecutionId} until {NotBefore:O}",
                    executionId, notBefore);
            }
            else if (await executions.TryMarkCancellationRequestedCancelledAsync(executionId, null, default)
                .ConfigureAwait(false))
            {
                outcome = OutcomeCancelled;
            }
        }
        catch (Exception ex)
        {
            outcome = OutcomeFailed;
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("error.type", ex.GetType().FullName);
            JobsTelemetry.FailuresTotal.Add(
                1,
                new KeyValuePair<string, object?>("job.type", execution.JobType),
                new KeyValuePair<string, object?>("reason_kind", "exception"));
            _logger.LogError(ex, "Job execution {ExecutionId} ({JobType}) failed", executionId, execution.JobType);
            var failedByThisRun = await TransitionFailureAsync(
                executions,
                transactionManager,
                jobManager,
                retryPolicy,
                execution,
                spec,
                failurePolicy,
                ex.Message)
                .ConfigureAwait(false);
            if (!failedByThisRun)
            {
                var cancelledByThisRun = await executions
                    .TryMarkCancellationRequestedCancelledAsync(executionId, ex.Message, default)
                    .ConfigureAwait(false);
                if (cancelledByThisRun)
                {
                    outcome = OutcomeCancelled;
                    activity?.AddEvent(new ActivityEvent("cancelled"));
                    _logger.LogInformation(
                        "An operator cancelled job execution {ExecutionId} ({JobType}), which ended with an error",
                        executionId,
                        execution.JobType);
                }
            }
        }
        finally
        {
            try
            {
                await heartbeatCts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                await heartbeatPump.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "The heartbeat pump for execution {ExecutionId} ended with an error", executionId);
            }

            _running.TryRemove(executionId, out _);
            CompleteShutdownTracking(executionId);
            jobCts.Dispose();

            JobsTelemetry.ExecutionDuration.Record(
                Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                new KeyValuePair<string, object?>("job.type", execution.JobType),
                new KeyValuePair<string, object?>("outcome", outcome));
        }
    }

    private static string GetTriggerMode(JobExecution execution)
    {
        if (execution.ScheduleTriggerId is not null)
        {
            return "schedule";
        }

        if (execution.EventTriggerId is not null)
        {
            return "event";
        }

        return execution.CreatedByUserId is not null ? "manual" : "system";
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
                    .TryMarkRunningFailedAsync(failed.Id, reason, transactionToken)
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

    private void CompleteShutdownTracking(Guid executionId)
    {
        if (_shutdownSignal.Outstanding.TryRemove(executionId, out var completion))
        {
            completion.TrySetResult();
        }
    }

    private async Task<bool> TransitionFailureAsync(
        IJobExecutionRepository executions,
        ITransactionManager transactionManager,
        IJobManager jobManager,
        IJobRetryPolicy retryPolicy,
        JobExecution failed,
        JobSpec spec,
        JobFailurePolicy policy,
        string reason)
    {
        var result = await TransitionRunningToFailureAsync(
            executions,
            transactionManager,
            jobManager,
            retryPolicy,
            failed,
            spec,
            policy,
            reason).ConfigureAwait(false);

        if (!result.Transitioned)
        {
            return false;
        }

        if (result.Retry is { } retry)
        {
            JobsTelemetry.RetriesTotal.Add(1, new KeyValuePair<string, object?>("job.type", failed.JobType));
            _logger.LogInformation(
                "The runtime queued {Policy} execution {NextExecutionId} for {ExecutionId} ({JobType}) as attempt " +
                "{Attempt} at {NotBeforeAt:O}",
                policy,
                retry.Id,
                failed.Id,
                failed.JobType,
                retry.Attempt,
                retry.NotBeforeAt);
        }
        else if (result.PublishFailure)
        {
            await _eventBus.PublishAsync(
                JobEventBuilder.BuildJobExecutionFailedEvent(failed.Id, failed.JobType, reason) with
                {
                    CorrelationId = failed.CorrelationId
                },
                default).ConfigureAwait(false);
        }

        return true;
    }

    private async Task RunHeartbeatPumpAsync(Guid executionId, CancellationToken cancellationToken)
    {
        await Task.Yield();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_heartbeatInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            bool owned;
            try
            {
                var scope = _scopeFactory.CreateAsyncScope();
                await using var scopeHandle = scope.ConfigureAwait(false);
                owned = await scope.ServiceProvider.GetRequiredService<IJobExecutionRepository>()
                    .TouchHeartbeatAsync(executionId, _nodeId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A failed write says nothing about ownership, so the job keeps running.
                _logger.LogWarning(
                    ex,
                    "The runtime failed to update heartbeat for execution {ExecutionId}",
                    executionId);
                continue;
            }

            if (owned)
            {
                continue;
            }

            _logger.LogWarning(
                "Execution {ExecutionId} is no longer owned by node {NodeId}, so the runtime cancels it",
                executionId,
                _nodeId);
            if (_running.TryGetValue(executionId, out var running))
            {
                running.LeaseLost = true;
                await running.Cts.CancelAsync().ConfigureAwait(false);
            }

            return;
        }
    }

    public async Task<bool> TryCancelAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        var locallyOwned = _running.TryGetValue(executionId, out var running);
        var scope = _scopeFactory.CreateAsyncScope();
        await using var scopeHandle = scope.ConfigureAwait(false);
        var executions = scope.ServiceProvider.GetRequiredService<IJobExecutionRepository>();
        var cancellation = await executions
            .CancelAsync(executionId, cancellationToken)
            .ConfigureAwait(false);

        if (cancellation is null)
        {
            if (!locallyOwned)
            {
                return false;
            }

            var persisted = await executions.GetByIdAsync(executionId, cancellationToken).ConfigureAwait(false);
            if (persisted?.Status is not JobExecutionStatus.CancellationRequested)
            {
                return false;
            }
        }

        if (cancellation?.CancelledBeforeStart is true)
        {
            return true;
        }

        if (locallyOwned)
        {
            running!.OperatorCancelled = true;
            try
            {
                await running.Cts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (cancellation is not null &&
            !string.Equals(cancellation.OwnerNodeId, _nodeId, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(cancellation.OwnerNodeId))
        {
            try
            {
                await _eventBus
                    .PublishAsync(
                        JobEventBuilder.BuildJobCancellationRequestedEvent(executionId, cancellation.OwnerNodeId),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "The runtime failed to signal the cancellation of execution {ExecutionId} owned by node {NodeId}",
                    executionId,
                    cancellation.OwnerNodeId);
            }

            _logger.LogInformation(
                "The runtime recorded a cancellation request for execution {ExecutionId} owned by node {NodeId}",
                executionId,
                cancellation.OwnerNodeId);
        }

        return true;
    }

    public void Dispose()
    {
        _disposing = true;
        foreach (var running in _running.Values)
        {
            try
            {
                running.Cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _running.Clear();
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
