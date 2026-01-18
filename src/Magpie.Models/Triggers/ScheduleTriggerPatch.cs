using System;
using System.Collections.Generic;

namespace Magpie.Models.Triggers;

/// <summary>
/// Patch for <see cref="ScheduleTrigger"/>.
/// </summary>
public sealed record ScheduleTriggerPatch
{
    /// <inheritdoc cref="ScheduleTrigger.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="ScheduleTrigger.Name" />
    public FieldPatch<string>? Name { get; init; }
    /// <inheritdoc cref="ScheduleTrigger.Schedule" />
    public FieldPatch<string>? Schedule { get; init; }
    /// <inheritdoc cref="ScheduleTrigger.Enabled" />
    public FieldPatch<bool>? Enabled { get; init; }
    /// <inheritdoc cref="ScheduleTrigger.Parameters" />
    public FieldPatch<IReadOnlyDictionary<string, string>?>? Parameters { get; init; }
}
