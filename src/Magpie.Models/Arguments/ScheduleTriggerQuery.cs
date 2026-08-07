using System;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for schedule triggers.
/// </summary>
public sealed record ScheduleTriggerQuery
{
    /// <summary>
    /// The due time that matches triggers with a next run at or before it.
    /// </summary>
    public DateTime? DueAsOf { get; init; }

    /// <summary>
    /// The id of the creating user to match.
    /// </summary>
    public Guid? CreatedByUserId { get; init; }

    /// <summary>
    /// The job type to match.
    /// </summary>
    public string? JobType { get; init; }

    /// <summary>
    /// The enabled state to match.
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// Whether a trigger has a next run, where <see langword="false"/> selects the triggers with no next run.
    /// </summary>
    public bool? HasNextRun { get; init; }
}
