using System;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a settings group.
/// </summary>
public sealed record SettingsGroupPatch
{
    /// <summary>
    /// The id of the extension that owns the group.
    /// </summary>
    public Guid? ExtensionId { get; init; }

    /// <summary>
    /// The name of the settings group.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The patch for the settings keys and values.
    /// </summary>
    public DictionaryPatch<string, string, string> Settings { get; init; } = new();
}
