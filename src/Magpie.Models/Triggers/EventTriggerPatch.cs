using System;
using System.Collections.Generic;

namespace Magpie.Models.Triggers;

/// <summary>
/// Patch for <see cref="EventTrigger"/>.
/// </summary>
public sealed record EventTriggerPatch
{
    /// <inheritdoc cref="EventTrigger.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="EventTrigger.Name" />
    public FieldPatch<string>? Name { get; init; }
    /// <inheritdoc cref="EventTrigger.EventName" />
    public FieldPatch<string>? EventName { get; init; }
    /// <inheritdoc cref="EventTrigger.Filter" />
    public FieldPatch<IReadOnlyDictionary<string, string>?>? Filter { get; init; }
    /// <inheritdoc cref="EventTrigger.Enabled" />
    public FieldPatch<bool>? Enabled { get; init; }
    /// <inheritdoc cref="EventTrigger.Parameters" />
    public FieldPatch<IReadOnlyDictionary<string, string>?>? Parameters { get; init; }
}
