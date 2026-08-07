using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for an event trigger.
/// </summary>
public sealed record EventTriggerPatch
{
    /// <summary>
    /// The id of the event trigger to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the display name of the trigger.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for the <see cref="EventFilter"/> that activates the trigger.
    /// </summary>
    public ScalarPatch<EventFilter>? EventFilter { get; init; }

    /// <summary>
    /// The patch for whether the trigger is enabled.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }

    /// <summary>
    /// The patch for the eligibility delay in seconds.
    /// </summary>
    public ScalarPatch<int>? DelaySeconds { get; init; }

    /// <summary>
    /// The patch for the job parameters.
    /// </summary>
    public DictionaryPatch<string, string, string>? Parameters { get; init; }
}
