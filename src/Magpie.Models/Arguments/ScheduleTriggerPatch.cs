using System;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="ScheduleTrigger"/>.
/// </summary>
public sealed record ScheduleTriggerPatch
{
    /// <summary>
    /// The <see cref="ScheduleTrigger.Id"/> of the item to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The <see cref="ScheduleTrigger.Name"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The <see cref="ScheduleTrigger.Schedule"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Schedule { get; init; }

    /// <summary>
    /// The <see cref="ScheduleTrigger.ExecutionMode"/> to patch.
    /// </summary>
    public ScalarPatch<JobExecutionMode>? ExecutionMode { get; init; }

    /// <summary>
    /// The <see cref="ScheduleTrigger.Enabled"/> to patch.
    /// </summary>
    public ScalarPatch<bool>? Enabled { get; init; }

    /// <summary>
    /// The <see cref="ScheduleTrigger.Parameters"/> to patch.
    /// </summary>
    public DictionaryPatch<string, string, string>? Parameters { get; init; }
}
