using System;
using System.Collections.Generic;

namespace Magpie.Models;

/// <summary>
/// The definition of a settings group that an extension owns.
/// </summary>
public sealed record ExtensionSettingsGroupDefinition
{
    /// <summary>
    /// The id of the extension that owns the settings group.
    /// </summary>
    public required Guid ExtensionId { get; init; }

    /// <summary>
    /// The name of the settings group.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The setting definitions of the group.
    /// </summary>
    public IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; init; } = [];
}
