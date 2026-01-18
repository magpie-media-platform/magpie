using System;
using System.Collections.Generic;

namespace Magpie.Models.Jobs;

/// <summary>
/// An execution instance of a job.
/// </summary>
/// <param name="Id">The unique identifier for this execution.</param>
/// <param name="JobTemplateId">The job template being executed.</param>
/// <param name="ScheduleTriggerId">The schedule trigger that triggered this, if scheduled.</param>
/// <param name="EventTriggerId">The event trigger that triggered this, if event-driven.</param>
/// <param name="EventId">The event that triggered this, if event-driven.</param>
/// <param name="Status">The current execution status.</param>
/// <param name="Progress">The progress from 0.0 to 1.0, if available.</param>
/// <param name="Message">A status message or error description.</param>
/// <param name="Parameters">The parameters passed to this execution.</param>
/// <param name="StartedAt">When the execution started.</param>
/// <param name="CompletedAt">When the execution completed, if finished.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record JobExecution(
    Guid Id,
    Guid JobTemplateId,
    Guid? ScheduleTriggerId,
    Guid? EventTriggerId,
    Guid? EventId,
    JobExecutionStatus Status,
    double? Progress,
    string? Message,
    IReadOnlyDictionary<string, string> Parameters,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
