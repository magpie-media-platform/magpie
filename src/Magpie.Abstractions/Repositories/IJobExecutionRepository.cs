using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for job executions.
/// </summary>
public interface IJobExecutionRepository :
    ICrudRepository<Guid, JobExecution>,
    IQueryRepository<JobExecution, JobExecutionQuery>,
    IBulkDeleteRepository<JobExecution, JobExecutionQuery>
{
    /// <summary>
    /// Creates a new job execution record.
    /// </summary>
    /// <param name="model">The execution to create</param>
    /// <param name="createdByUserId">
    /// The id of the user that creates the record, or <c>null</c> when no user started it.
    /// </param>
    /// <param name="clearDeferralOnCoalesce">
    /// Whether a pending execution loses its deferral time when this execution coalesces into it.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<JobExecution> CreateAsync(
        JobExecution model,
        Guid? createdByUserId,
        bool clearDeferralOnCoalesce,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a page of executions that a node can claim.
    /// </summary>
    /// <param name="paging">The paging parameters</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<JobClaimCandidate>> GetClaimableCandidatesAsync(
        PagingParameter paging,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims an execution for a node.
    /// </summary>
    /// <param name="executionId">The id of the job execution</param>
    /// <param name="nodeId">The id of the node that claims the execution</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<JobExecution?> TryClaimAsync(Guid executionId, string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a claimed execution to the running state.
    /// </summary>
    /// <param name="executionId">The id of the job execution</param>
    /// <param name="nodeId">The id of the node that holds the claim</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> TryStartAsync(Guid executionId, string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an execution as failed, only while it runs.
    /// </summary>
    /// <param name="executionId">The id of the job execution</param>
    /// <param name="errorMessage">The text that describes the failure</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> TryMarkRunningFailedAsync(
        Guid executionId,
        string errorMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a request to cancel an execution.
    /// </summary>
    /// <param name="executionId">The id of the job execution</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<JobCancellationRequestResult?> RequestCancellationAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the heartbeat time of a running execution.
    /// </summary>
    /// <param name="executionId">The id of the job execution</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task TouchHeartbeatAsync(Guid executionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the timeout deadline of a running execution that a node owns.
    /// </summary>
    /// <param name="executionId">The id of the job execution</param>
    /// <param name="nodeId">The id of the node that owns the running execution</param>
    /// <param name="timeoutAt">The UTC time after which the execution counts as timed out</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> TrySetRunningTimeoutAsync(
        Guid executionId,
        string nodeId,
        DateTime timeoutAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the progress and the status message of an execution.
    /// </summary>
    /// <param name="executionId">The id of the job execution</param>
    /// <param name="progress">The progress from 0.0 to 1.0, or <c>null</c> to keep the stored value</param>
    /// <param name="statusMessage">The status message, or <c>null</c> to keep the stored value</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> UpdateProgressAsync(
        Guid executionId,
        double? progress,
        string? statusMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the time of the last attempt in each lock partition.
    /// </summary>
    /// <param name="jobType">The name of the job type</param>
    /// <param name="lockPartitions">The lock partition keys to look up</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyDictionary<string, DateTime>> GetLastAttemptTimesByLockPartitionAsync(
        string jobType,
        IReadOnlyCollection<string> lockPartitions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the ancestors and the descendants of an execution.
    /// </summary>
    /// <param name="executionId">The id of the job execution</param>
    /// <param name="pageSize">The number of executions to read at a time</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<JobExecution>> GetLineageAsync(
        Guid executionId,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an execution as complete, only while it runs.
    /// </summary>
    /// <param name="executionId">The id of the job execution</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> TryMarkRunningCompletedAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an execution as failed, only while it waits in the queue.
    /// </summary>
    /// <param name="executionId">The id of the job execution</param>
    /// <param name="errorMessage">The text that describes the failure</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> TryMarkQueuedFailedAsync(
        Guid executionId,
        string errorMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an execution as cancelled, only after a cancellation request.
    /// </summary>
    /// <param name="executionId">The id of the job execution</param>
    /// <param name="reason">The reason for the cancellation, or <c>null</c> when none exists</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> TryMarkCancellationRequestedCancelledAsync(
        Guid executionId,
        string? reason = null,
        CancellationToken cancellationToken = default);
}
