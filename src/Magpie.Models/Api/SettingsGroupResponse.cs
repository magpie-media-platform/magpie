using System;
using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models.Api;

/// <summary>
/// The read response for a settings group.
/// </summary>
public sealed record SettingsGroupResponse
{
    /// <summary>
    /// The name of the settings group.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The id of the extension that owns the group, or <see langword="null"/> for a system group.
    /// </summary>
    public Guid? ExtensionId { get; init; }

    /// <summary>
    /// The settings whose values are not secret.
    /// </summary>
    public IReadOnlyList<Setting> Settings { get; init; } = [];

    /// <summary>
    /// The presence state of each secret setting, keyed by setting key.
    /// </summary>
    public IReadOnlyDictionary<string, SecretSettingState> SecretSettings { get; init; } =
        new Dictionary<string, SecretSettingState>();

    /// <summary>
    /// The declared schema for the keys of the group, or an empty list when no schema exists.
    /// </summary>
    public IReadOnlyList<SettingSchemaDefinition> Schema { get; init; } = [];

    /// <summary>
    /// Whether Magpie resolved the schema for the group.
    /// </summary>
    public bool SchemaResolved { get; init; } = true;

    /// <summary>
    /// Whether the settings-group test endpoint accepts this group.
    /// </summary>
    public bool CanTest { get; init; }
}
