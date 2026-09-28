using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;

namespace Magpie.Sdk.Services;

/// <summary>
/// The management of job execution.
/// </summary>
public interface IJobManager
{
    /// <summary>
    /// Resolves the concurrency partition for a job and its parameters.
    /// </summary>
    /// <param name="jobType">The job type</param>
    /// <param name="parameters">The job parameters</param>
    string? ResolveLockPartition(string jobType, IReadOnlyDictionary<string, string?> parameters);

    /// <summary>
    /// Runs a trusted job request.
    /// </summary>
    /// <param name="request">The job request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Guid> ExecuteAsync(JobEnqueueRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a job for a caller.
    /// </summary>
    /// <param name="request">The job request</param>
    /// <param name="callerUserId">The caller id</param>
    /// <param name="callerRole">The caller role</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Guid> EnqueueAsync(
        JobEnqueueRequest request,
        Guid callerUserId,
        Role callerRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a job immediately.
    /// </summary>
    /// <param name="request">The job request</param>
    /// <param name="callerUserId">The caller id</param>
    /// <param name="callerRole">The caller role</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<JobExecution> ExecuteImmediateAsync(
        JobEnqueueRequest request,
        Guid callerUserId,
        Role callerRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries job executions.
    /// </summary>
    /// <param name="query">The query</param>
    /// <param name="paging">The paging parameters</param>
    /// <param name="sort">The sort parameters</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<PagedList<JobExecution>> QueryExecutionsAsync(
        JobExecutionQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A job execution.
    /// </summary>
    /// <param name="executionId">The execution id</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<JobExecution?> GetExecutionAsync(Guid executionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A job execution tree.
    /// </summary>
    /// <param name="executionId">The execution id</param>
    /// <param name="includeSiblings">Whether to include sibling branches</param>
    /// <param name="maxNodes">The maximum number of executions in the tree</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<JobExecutionTree?> GetExecutionTreeAsync(
        Guid executionId,
        bool includeSiblings,
        int maxNodes = 500,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a job execution.
    /// </summary>
    /// <param name="executionId">The id of the job execution</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>Whether the job execution was cancelled</returns>
    Task<bool> CancelAsync(Guid executionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists registered job specifications.
    /// </summary>
    /// <param name="userTriggerMode">The trigger mode, or <see langword="null"/> for all</param>
    IReadOnlyList<JobSpec> GetSpecs(JobTriggerMode? userTriggerMode = null);

    /// <summary>
    /// Lists schedule triggers.
    /// </summary>
    /// <param name="mineOnly">Whether to return only triggers owned by the caller</param>
    /// <param name="callerUserId">The caller id</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<ScheduleTrigger>> ListScheduleTriggersAsync(
        bool mineOnly,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A schedule trigger.
    /// </summary>
    /// <param name="triggerId">The trigger id</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ScheduleTrigger?> GetScheduleTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a schedule trigger.
    /// </summary>
    /// <param name="create">The trigger values</param>
    /// <param name="ownerUserId">The owner id</param>
    /// <param name="ownerRole">The owner role</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ScheduleTrigger> CreateScheduleTriggerAsync(
        ScheduleTriggerCreate create,
        Guid ownerUserId,
        Role ownerRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a schedule trigger.
    /// </summary>
    /// <param name="triggerId">The trigger id</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="callerUserId">The caller id</param>
    /// <param name="callerRole">The caller role</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchScheduleTriggerAsync(
        Guid triggerId,
        ScheduleTriggerPatch patch,
        Guid callerUserId,
        Role callerRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a schedule trigger.
    /// </summary>
    /// <param name="triggerId">The trigger id</param>
    /// <param name="callerUserId">The caller id</param>
    /// <param name="callerRole">The caller role</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteScheduleTriggerAsync(
        Guid triggerId,
        Guid callerUserId,
        Role callerRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists event triggers.
    /// </summary>
    /// <param name="extensionId">The extension id, or <see langword="null"/> for all</param>
    /// <param name="enabled">The enabled state, or <see langword="null"/> for all</param>
    /// <param name="mineOnly">Whether to return only triggers owned by the caller</param>
    /// <param name="callerUserId">The caller id</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<EventTrigger>> ListEventTriggersAsync(
        Guid? extensionId,
        bool? enabled,
        bool mineOnly,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// An event trigger.
    /// </summary>
    /// <param name="triggerId">The trigger id</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<EventTrigger?> GetEventTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an event trigger.
    /// </summary>
    /// <param name="create">The trigger values</param>
    /// <param name="ownerUserId">The owner id</param>
    /// <param name="ownerRole">The owner role</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<EventTrigger> CreateEventTriggerAsync(
        EventTriggerCreate create,
        Guid ownerUserId,
        Role ownerRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches an event trigger.
    /// </summary>
    /// <param name="triggerId">The trigger id</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="callerUserId">The caller id</param>
    /// <param name="callerRole">The caller role</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchEventTriggerAsync(
        Guid triggerId,
        EventTriggerPatch patch,
        Guid callerUserId,
        Role callerRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an event trigger.
    /// </summary>
    /// <param name="triggerId">The trigger id</param>
    /// <param name="callerUserId">The caller id</param>
    /// <param name="callerRole">The caller role</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteEventTriggerAsync(
        Guid triggerId,
        Guid callerUserId,
        Role callerRole,
        CancellationToken cancellationToken = default);
}
