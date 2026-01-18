using System;

namespace Magpie.Models.Configuration;

/// <summary>
/// A key/value setting within a settings group.
/// </summary>
/// <param name="SettingsGroupId">The settings group that contains this setting.</param>
/// <param name="Key">The setting key.</param>
/// <param name="Value">The setting value.</param>
public sealed record Setting(
    Guid SettingsGroupId,
    string Key,
    string Value);
