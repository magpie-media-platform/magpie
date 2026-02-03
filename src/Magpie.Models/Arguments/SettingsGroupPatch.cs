using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="SettingsGroup"/>.
/// </summary>
public sealed record SettingsGroupPatch
{
    /// <summary>
    /// The <see cref="SettingsGroup.ExtensionId"/> of the item to patch.
    /// </summary>
    public required Guid? ExtensionId { get; init; }

    /// <summary>
    /// The <see cref="SettingsGroup.Name"/> of the item to patch.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The <see cref="SettingsGroup.Settings"/> to patch.
    /// </summary>
    public DictionaryPatch<string, string, string> Settings { get; init; } = new();
}
