using System;

using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// Criteria for filtering job executions.
/// </summary>d
public record JobExecutionQuery
{
    /// <summary>
    /// The job type.
    /// </summary>
    public string? JobType { get; init; }

    /// <summary>
    /// The extension that provides the job.
    /// </summary>
    public Guid? ExtensionId { get; init; }

    /// <summary>
    /// The schedule trigger id.
    /// </summary>
    public Guid? ScheduleTriggerId { get; init; }

    /// <summary>
    /// The event trigger id.
    /// </summary>
    public Guid? EventTriggerId { get; init; }

    /// <summary>
    /// The job execution status.
    /// </summary>
    public JobExecutionStatus? Status { get; init; }

    /// <summary>
    /// The timestamp range.
    /// </summary>
    public Range<DateTime>? Timestamp { get; init; }

    /// <summary>
    /// the last heartbeat range.
    /// </summary>
    public Range<DateTime>? LastHeartbeat { get; init; }
}
