using System;

using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// Criteria for filtering job executions.
/// </summary>
/// <param name="JobType">The job type.</param>
/// <param name="ExtensionId">The extension that provides the job.</param>
/// <param name="ScheduleTriggerId">The schedule trigger id.</param>
/// <param name="EventTriggerId">The event trigger id.</param>
/// <param name="Status">The job execution status.</param>
/// <param name="Timestamp">The timestamp range.</param>
/// <param name="LastHeartbeat">the last heartbeat range.</param>
public record JobExecutionQuery(
    string? JobType = null,
    Guid? ExtensionId = null,
    Guid? ScheduleTriggerId = null,
    Guid? EventTriggerId = null,
    JobExecutionStatus? Status = null,
    Range<DateTime>? Timestamp = null,
    Range<DateTime>? LastHeartbeat = null);
