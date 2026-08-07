using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
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
    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan DefaultMaxRunDuration = TimeSpan.FromHours(1);

    private readonly IJobExecutionScopeFactory _jobExecutionScopeFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobRegistry _registry;
    private readonly IEventBus _eventBus;
    private readonly ILogger<DefaultJobRuntime> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _heartbeatInterval;
    private readonly ConcurrentDictionary<Guid, RunningExecution> _running = new();
    private string _nodeId;

    public DefaultJobRuntime(
        IJobExecutionScopeFactory jobExecutionScopeFactory,
        IServiceScopeFactory scopeFactory,
        IJobRegistry registry,
        MagpieHostEnvironment hostEnvironment,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<DefaultJobRuntime> logger)
        : this(
            jobExecutionScopeFactory,
            scopeFactory,
            registry,
            hostEnvironment,
            eventBus,
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
        TimeProvider timeProvider,
        ILogger<DefaultJobRuntime> logger,
        TimeSpan heartbeatInterval)
    {
        _jobExecutionScopeFactory = jobScopes;
        _scopeFactory = scopeFactory;
        _registry = registry;
        _eventBus = eventBus;
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
        var auditRecords = repos.ServiceProvider.GetRequiredService<IAuditRecordRepository>();
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
                "No job handler exists for job type {JobType}. The runtime will mark execution {ExecutionId} as failed",
                execution.JobType,
                executionId);
            var noHandlerReason = $"No handler registered for job type {execution.JobType}";
            var failedByThisRun = execution.Status is JobExecutionStatus.Running
                ? await executions.TryMarkRunningFailedAsync(executionId, noHandlerReason, default)
                    .ConfigureAwait(false)
                : await executions.TryMarkQueuedFailedAsync(executionId, noHandlerReason, default)
                    .ConfigureAwait(false);
            if (failedByThisRun)
            {
                await _eventBus.PublishAsync(
                    JobEventBuilder.BuildJobExecutionFailedEvent(execution.Id, execution.JobType, noHandlerReason),
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
                    "This node failed to claim job execution {ExecutionId}. Another node took it, or " +
                    "another execution holds the same lock partition",
                    executionId);
                return;
            }
        }

        var failurePolicy = spec.FailurePolicy;
        var declaredTimeout = spec.Timeout is { } specTimeout && specTimeout > TimeSpan.Zero
            ? specTimeout
            : default(TimeSpan?);
        var effectiveTimeout = declaredTimeout ?? DefaultMaxRunDuration;

        await executions
            .TrySetRunningTimeoutAsync(
                executionId,
                _nodeId,
                _timeProvider.GetUtcNow().UtcDateTime + effectiveTimeout,
                default)
            .ConfigureAwait(false);

        var jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runningEntry = new RunningExecution(jobCts);
        _running[executionId] = runningEntry;

        JobExecution? registeredState;
        try
        {
            registeredState = await executions.GetByIdAsync(executionId, default).ConfigureAwait(false);
        }
        catch
        {
            _running.TryRemove(executionId, out _);
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
            jobCts.Dispose();
            return;
        }

        using var heartbeatCts = new CancellationTokenSource();
        var heartbeatPump = RunHeartbeatPumpAsync(executionId, heartbeatCts.Token);

        try
        {
            var jobExecutionScope = _jobExecutionScopeFactory.CreateScope(executionId);

            await using var _ = jobExecutionScope.ConfigureAwait(false);
            jobExecutionScope.SetParentContext(executionId);

            if (!jobExecutionScope.TryGetHandler(spec, out var handler))
            {
                _logger.LogError(
                    "No job handler exists for job type {JobType}. " +
                    "The runtime will mark execution {ExecutionId} as failed",
                    execution.JobType,
                    executionId);
                var noHandlerReason = $"No handler registered for job type {execution.JobType}";
                var failedByThisRun = await executions
                    .TryMarkRunningFailedAsync(executionId, noHandlerReason, default)
                    .ConfigureAwait(false);
                if (failedByThisRun)
                {
                    await _eventBus.PublishAsync(
                        JobEventBuilder.BuildJobExecutionFailedEvent(execution.Id, execution.JobType, noHandlerReason),
                        default).ConfigureAwait(false);
                }

                return;
            }

            jobCts.CancelAfter(effectiveTimeout);

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
                    _logger.LogInformation(
                        "An operator cancelled job execution {ExecutionId} ({JobType})",
                        executionId,
                        execution.JobType);
                }
            }

            _logger.LogDebug(
                "The job handler returned for execution {ExecutionId} ({JobType})",
                executionId,
                execution.JobType);
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested)
        {
            var hostShutdown = cancellationToken.IsCancellationRequested;
            if (runningEntry.OperatorCancelled)
            {
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
            else if (!hostShutdown)
            {
                var seconds = effectiveTimeout.TotalSeconds;
                var reason = $"Timed out after {seconds:N0}s";
                var failedByThisRun = await TransitionFailureAsync(
                    executions,
                    auditRecords,
                    transactionManager,
                    jobManager,
                    retryPolicy,
                    execution,
                    spec,
                    failurePolicy,
                    reason,
                    timeoutAudit: true)
                    .ConfigureAwait(false);
                if (failedByThisRun)
                {
                    _logger.LogWarning(
                        "Job execution {ExecutionId} ({JobType}) timed out after {TimeoutSeconds:N0}s",
                        executionId,
                        execution.JobType,
                        seconds);
                }
            }
            else if (hostShutdown)
            {
                const string reason = "Interrupted by host shutdown";
                var failedByThisRun = await TransitionFailureAsync(
                    executions,
                    auditRecords,
                    transactionManager,
                    jobManager,
                    retryPolicy,
                    execution,
                    spec,
                    failurePolicy,
                    reason,
                    timeoutAudit: false)
                    .ConfigureAwait(false);
                if (failedByThisRun)
                {
                    _logger.LogWarning(
                        "The host shutdown interrupted execution {ExecutionId} ({JobType}). " +
                        "The runtime applied the {Policy} policy",
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
                        _logger.LogInformation(
                            "An operator cancelled job execution {ExecutionId} ({JobType})",
                            executionId,
                            execution.JobType);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job execution {ExecutionId} ({JobType}) failed", executionId, execution.JobType);
            await TransitionFailureAsync(
                executions,
                auditRecords,
                transactionManager,
                jobManager,
                retryPolicy,
                execution,
                spec,
                failurePolicy,
                ex.Message,
                timeoutAudit: false)
                .ConfigureAwait(false);
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
            jobCts.Dispose();
        }
    }

    private async Task<bool> TransitionFailureAsync(
        IJobExecutionRepository executions,
        IAuditRecordRepository auditRecords,
        ITransactionManager transactionManager,
        IJobManager jobManager,
        IJobRetryPolicy retryPolicy,
        JobExecution failed,
        JobSpec spec,
        JobFailurePolicy policy,
        string reason,
        bool timeoutAudit)
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

                if (timeoutAudit)
                {
                    await auditRecords.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Jobs,
                            Operation = "JobTimeout",
                            EntityId = failed.Id.ToString(),
                            EntityType = nameof(JobExecution),
                            Detail = $"Job execution {failed.Id} ({failed.JobType}) timed out. {reason}",
                            UserId = failed.CreatedByUserId,
                            CorrelationId = failed.CorrelationId
                        },
                        transactionToken).ConfigureAwait(false);
                }

                if (policy is JobFailurePolicy.Fail ||
                    (policy is JobFailurePolicy.Retry &&
                        failed.ExecutionMode is JobExecutionMode.Immediate))
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
                    ?? jobManager.ResolveLockPartition(
                        failed.JobType,
                        failed.GetLockPartitionFor(spec));
                await executions.CreateAsync(retry, failed.CreatedByUserId, transactionToken).ConfigureAwait(false);
                result = new(true, PublishFailure: false, retry);
            },
            default).ConfigureAwait(false);

        if (!result.Transitioned)
        {
            return false;
        }

        if (result.Retry is { } retry)
        {
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
                JobEventBuilder.BuildJobExecutionFailedEvent(failed.Id, failed.JobType, reason),
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
                await Task.Delay(_heartbeatInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var scope = _scopeFactory.CreateAsyncScope();
                await using var scopeHandle = scope.ConfigureAwait(false);
                await scope.ServiceProvider.GetRequiredService<IJobExecutionRepository>()
                    .TouchHeartbeatAsync(executionId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "The runtime failed to update heartbeat for execution {ExecutionId}", executionId);
            }
        }
    }

    public async Task<bool> TryCancelAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        var locallyOwned = _running.TryGetValue(executionId, out var running);
        var scope = _scopeFactory.CreateAsyncScope();
        await using var scopeHandle = scope.ConfigureAwait(false);
        var executions = scope.ServiceProvider.GetRequiredService<IJobExecutionRepository>();
        var cancellation = await executions
            .RequestCancellationAsync(executionId, cancellationToken)
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

    private sealed class RunningExecution(CancellationTokenSource cts)
    {
        public CancellationTokenSource Cts { get; } = cts;
        public bool OperatorCancelled { get; set; }
    }
}
