using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// The context of one job execution.
/// </summary>
/// <param name="ExecutionId">The id of this job execution</param>
/// <param name="ScheduleTriggerId">The id of the schedule trigger that started the job, when any</param>
/// <param name="EventTriggerId">The id of the event trigger that started the job, when any</param>
/// <param name="ManualTriggerUserId">The id of the user who started the job by hand, when any</param>
/// <param name="ExecutionMode">The mode of this execution</param>
/// <param name="Parameters">The parameter values for this execution, keyed by parameter name</param>
/// <param name="Event">The event that started the job, when any</param>
/// <param name="CorrelationId">The correlation id that the original trigger set for the job chain</param>
/// <param name="Deadline">The time the runtime cancels this execution for its run-time budget, when known</param>
public sealed record JobContext(
    Guid ExecutionId,
    Guid? ScheduleTriggerId,
    Guid? EventTriggerId,
    Guid? ManualTriggerUserId,
    JobExecutionMode ExecutionMode,
    IReadOnlyDictionary<string, string> Parameters,
    MagpieEvent? Event,
    Guid? CorrelationId = null,
    DateTimeOffset? Deadline = null);
