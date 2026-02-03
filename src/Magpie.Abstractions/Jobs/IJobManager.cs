using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

namespace Magpie.Abstractions.Jobs;

/// <summary>
/// High-level interface for job management.
/// </summary>
public interface IJobManager
{
    /// <summary>
    /// Manually execute a job.
    /// </summary>
    /// <param name="jobType">The job type to execute.</param>
    /// <param name="extensionId">The extension that provides the job, or null for system jobs.</param>
    /// <param name="parameters">Parameters to pass to the job.</param>
    /// <param name="mode">The execution mode.</param>
    /// <param name="userId">The user triggering the execution, or null for system-triggered jobs.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The execution id.</returns>
    Task<Guid> ExecuteAsync(
        string jobType,
        Guid? extensionId,
        IReadOnlyDictionary<string, string>? parameters,
        JobExecutionMode mode,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query job executions.
    /// </summary>
    /// <param name="query">The query criteria.</param>
    /// <param name="paging">The paging parameters.</param>
    /// <param name="sort">The sort parameters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The paged list of job executions.</returns>
    Task<PagedList<JobExecution>> QueryExecutionsAsync(
        JobExecutionQuery query,
        PagingParameter? paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a job execution by id.
    /// </summary>
    /// <param name="executionId">The execution id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The job execution, or null if not found.</returns>
    Task<JobExecution?> GetExecutionAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Request cancellation of a running job.
    /// </summary>
    /// <param name="executionId">The execution id to cancel.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the cancellation was requested, otherwise <see langword="false"/>.</returns>
    Task<bool> RequestCancellationAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a schedule trigger.
    /// </summary>
    /// <param name="create">The trigger creation arguments.</param>
    /// <param name="userId">The user creating the trigger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created trigger.</returns>
    Task<ScheduleTrigger> CreateScheduleTriggerAsync(
        ScheduleTriggerCreate create,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a schedule trigger.
    /// </summary>
    /// <param name="patch">The trigger patch.</param>
    /// <param name="userId">The user updating the trigger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if updated, otherwise <see langword="false"/>.</returns>
    Task<bool> UpdateScheduleTriggerAsync(
        ScheduleTriggerPatch patch,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create an event trigger.
    /// </summary>
    /// <param name="create">The trigger creation arguments.</param>
    /// <param name="userId">The user creating the trigger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created trigger.</returns>
    Task<EventTrigger> CreateEventTriggerAsync(
        EventTriggerCreate create,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an event trigger.
    /// </summary>
    /// <param name="patch">The trigger patch.</param>
    /// <param name="userId">The user updating the trigger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if updated, otherwise <see langword="false"/>.</returns>
    Task<bool> UpdateEventTriggerAsync(
        EventTriggerPatch patch,
        Guid? userId,
        CancellationToken cancellationToken = default);
}
