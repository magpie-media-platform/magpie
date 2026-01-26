using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Jobs;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing job executions.
/// </summary>
public interface IJobExecutionRepository : ICrudRepository<Guid, JobExecution>
{
    /// <summary>
    /// Get all job executions for a specific job template.
    /// </summary>
    /// <param name="jobTemplateId">The job template identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<JobExecution>>> GetByJobTemplateIdAsync(
        Guid jobTemplateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all job executions triggered by a specific schedule trigger.
    /// </summary>
    /// <param name="scheduleTriggerId">The schedule trigger identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<JobExecution>>> GetByScheduleTriggerIdAsync(
        Guid scheduleTriggerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all job executions triggered by a specific event trigger.
    /// </summary>
    /// <param name="eventTriggerId">The event trigger identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<JobExecution>>> GetByEventTriggerIdAsync(
        Guid eventTriggerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all job executions in a specific status.
    /// </summary>
    /// <param name="status">The execution status to filter by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<JobExecution>>> GetByStatusAsync(
        JobExecutionStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the status and progress of a job execution.
    /// </summary>
    /// <param name="id">The job execution identifier.</param>
    /// <param name="status">The new status.</param>
    /// <param name="progress">The progress from 0.0 to 1.0, if available.</param>
    /// <param name="message">A status message or error description.</param>
    /// <param name="completedAt">The completion time, if finished.</param>
    /// <param name="modifiedByUserId">The id of the user modifying the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing <see langword="true"/> if the execution was updated,
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<Result<bool>> UpdateStatusAsync(
        Guid id,
        JobExecutionStatus status,
        double? progress,
        string? message,
        DateTimeOffset? completedAt,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all job executions for a specific job template.
    /// </summary>
    /// <param name="jobTemplateId">The job template identifier.</param>
    /// <param name="deletedByUserId">The id of the user deleting the executions.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of executions deleted.</returns>
    Task<Result<int>> DeleteByJobTemplateIdAsync(
        Guid jobTemplateId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default);
}
