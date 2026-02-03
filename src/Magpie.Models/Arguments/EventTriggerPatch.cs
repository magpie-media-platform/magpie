using System;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="EventTrigger"/>.
/// </summary>
public sealed record EventTriggerPatch
{
    /// <summary>
    /// The <see cref="EventTrigger.Id"/> of the item to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The <see cref="EventTrigger.Name"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The <see cref="EventTrigger.EventFilter"/> to patch.
    /// </summary>
    public ScalarPatch<EventFilter>? EventFilter { get; init; }

    /// <summary>
    /// The <see cref="EventTrigger.ExecutionMode"/> to patch.
    /// </summary>
    public ScalarPatch<JobExecutionMode>? ExecutionMode { get; init; }

    /// <summary>
    /// The <see cref="EventTrigger.Enabled"/> to patch.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }

    /// <summary>
    /// The <see cref="EventTrigger.Parameters"/> to patch.
    /// </summary>
    public DictionaryPatch<string, string, string>? Parameters { get; init; }
}
