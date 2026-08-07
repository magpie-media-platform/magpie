using System;

using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a schedule trigger.
/// </summary>
public sealed record ScheduleTriggerPatch
{
    /// <summary>
    /// The id of the schedule trigger to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the display name of the trigger.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for the cron schedule.
    /// </summary>
    public ScalarPatch<string>? Schedule { get; init; }

    /// <summary>
    /// The patch for the execution mode.
    /// </summary>
    public ScalarPatch<JobExecutionMode>? ExecutionMode { get; init; }

    /// <summary>
    /// The patch for whether the trigger is enabled.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }

    /// <summary>
    /// The patch for the job parameters.
    /// </summary>
    public DictionaryPatch<string, string, string>? Parameters { get; init; }
}
