using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// Context provided to a job during execution.
/// </summary>
/// <param name="ExecutionId">The unique id for this execution.</param>
/// <param name="Parameters">Parameters passed to the job.</param>
/// <param name="EventTriggerId">The id of the event trigger that caused this job to run, if applicable.</param>
/// <param name="ScheduleTriggerId">The id of the schedule trigger that caused this job to run, if applicable.</param>
/// <param name="ManualTriggerUserId">The id of the user who manually triggered this job, if applicable.</param>
/// <param name="ExecutionMode">The mode in which the job is being executed.</param>
/// <param name="Event">The event that triggered this, if event-driven.</param>
public sealed record JobContext(
    Guid ExecutionId,
    Guid? ScheduleTriggerId,
    Guid? EventTriggerId,
    Guid? ManualTriggerUserId,
    JobExecutionMode ExecutionMode,
    IReadOnlyDictionary<string, string> Parameters, MagpieEvent? Event);
