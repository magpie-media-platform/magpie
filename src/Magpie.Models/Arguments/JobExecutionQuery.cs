using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for job executions.
/// </summary>
public sealed record JobExecutionQuery
{
    /// <summary>
    /// The job type to match.
    /// </summary>
    public string? JobType { get; init; }

    /// <summary>
    /// The set of job types to match.
    /// </summary>
    public IReadOnlyCollection<string>? JobTypes { get; init; }

    /// <summary>
    /// The id of the extension to match.
    /// </summary>
    public Guid? ExtensionId { get; init; }

    /// <summary>
    /// The id of the schedule trigger to match.
    /// </summary>
    public Guid? ScheduleTriggerId { get; init; }

    /// <summary>
    /// The id of the event trigger to match.
    /// </summary>
    public Guid? EventTriggerId { get; init; }

    /// <summary>
    /// The id of the worker node to match.
    /// </summary>
    public string? NodeId { get; init; }

    /// <summary>
    /// The execution status to match.
    /// </summary>
    public JobExecutionStatus? Status { get; init; }

    /// <summary>
    /// The set of execution statuses to match.
    /// </summary>
    public IReadOnlyCollection<JobExecutionStatus>? Statuses { get; init; }

    /// <summary>
    /// Whether to match only queued executions that are not yet eligible to run.
    /// </summary>
    public bool? Pending { get; init; }

    /// <summary>
    /// The correlation id to match.
    /// </summary>
    public Guid? CorrelationId { get; init; }

    /// <summary>
    /// Whether to match only root executions.
    /// </summary>
    public bool RootsOnly { get; init; }

    /// <summary>
    /// The id of the creating user to match.
    /// </summary>
    public Guid? CreatedByUserId { get; init; }

    /// <summary>
    /// The range of creation times to match.
    /// </summary>
    public Range<DateTime>? Timestamp { get; init; }

    /// <summary>
    /// The range of completion times to match.
    /// </summary>
    public Range<DateTime>? CompletedAt { get; init; }

    /// <summary>
    /// The range of execution timestamps to match.
    /// </summary>
    public Range<DateTime>? ExecutionTimestamp { get; init; }

    /// <summary>
    /// The parameter or trigger context values to match.
    /// </summary>
    public IReadOnlyCollection<string>? ParameterValueReferences { get; init; }

    /// <summary>
    /// The range of last heartbeat times to match.
    /// </summary>
    public Range<DateTime>? LastHeartbeat { get; init; }
}
