using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models;

/// <summary>
/// A settings group with the schema declared for it.
/// </summary>
public sealed record SettingsGroupDetail
{
    /// <summary>
    /// The settings group with its raw stored values. A wire consumer must omit the secret-flagged values.
    /// </summary>
    public required SettingsGroup Group { get; init; }

    /// <summary>
    /// The declared schema for the keys of the group, or an empty list when the group declares none.
    /// </summary>
    public IReadOnlyList<SettingSchemaDefinition> Schema { get; init; } = [];

    /// <summary>
    /// Whether the schema resolved. When false, a consumer cannot identify the secret keys and must withhold every value.
    /// </summary>
    public bool SchemaResolved { get; init; } = true;

    /// <summary>
    /// Whether the settings group supports a connection test.
    /// </summary>
    public bool CanTest { get; init; }
}
