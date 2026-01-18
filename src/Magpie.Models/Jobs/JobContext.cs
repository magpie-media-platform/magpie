using System;
using System.Collections.Generic;

using Magpie.Models.Events;

namespace Magpie.Models.Jobs;

/// <summary>
/// Context provided to a job during execution.
/// </summary>
/// <param name="ExecutionId">The unique identifier for this execution.</param>
/// <param name="JobTemplateId">The job template being executed.</param>
/// <param name="ScheduleTriggerId">The schedule trigger that triggered this, if scheduled.</param>
/// <param name="EventTriggerId">The event trigger that triggered this, if event-driven.</param>
/// <param name="Event">The event that triggered this, if event-driven.</param>
/// <param name="Parameters">Parameters passed to the job.</param>
public sealed record JobContext(
    Guid ExecutionId,
    Guid JobTemplateId,
    Guid? ScheduleTriggerId,
    Guid? EventTriggerId,
    MagpieEvent? Event,
    IReadOnlyDictionary<string, string> Parameters);
