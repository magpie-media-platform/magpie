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
    /// Creates an execution for an inactive lock partition.
    /// </summary>
    /// <param name="model">The queued execution with a lock partition</param>
    /// <param name="createdByUserId">The id of the user that starts the execution</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<JobExecution> CreateIfInactiveAsync(
        JobExecution model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The repository does not support atomic inactive-only enqueue.");

    /// <summary>
    /// Gets a page of executions that a node can claim, oldest first, with an equal share for each job type.
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
    /// Cancels a job execution.
    /// </summary>
    /// <param name="executionId">The id of the job execution</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The job cancellation, or null when the job execution was not cancelled</returns>
    Task<JobCancellation?> CancelAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a job execution.
    /// </summary>
    /// <param name="execution">The job execution as it was read</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The job cancellation, or null when the job execution was not cancelled</returns>
    Task<JobCancellation?> CancelAsync(
        JobExecution execution,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the heartbeat time of a running execution that the node owns.
    /// </summary>
    /// <param name="executionId">The id of the job execution</param>
    /// <param name="nodeId">The id of the node that runs the execution</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the node still owns the execution, otherwise <see langword="false"/>.</returns>
    Task<bool> TouchHeartbeatAsync(Guid executionId, string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers an inactive execution only if its status, owner and heartbeat have not changed.
    /// </summary>
    /// <param name="expected">The execution observed by the recovery scan</param>
    /// <param name="reason">The reason for recovery</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> TryRecoverInactiveAsync(
        JobExecution expected,
        string reason,
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
    /// <param name="maxNodes">The maximum number of executions to return</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<JobExecution>> GetLineageAsync(
        Guid executionId,
        int pageSize,
        int maxNodes = 500,
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
