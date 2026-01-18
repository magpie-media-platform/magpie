using System;

namespace Magpie.Models.Configuration;

/// <summary>
/// Patch for updating settings in a settings group.
/// </summary>
/// <param name="ExtensionId">The extension ID.</param>
/// <param name="Name">The settings group name that identifies which group to update.</param>
/// <param name="Settings">The setting patches to apply.</param>
public sealed record SettingsGroupPatch(
    Guid? ExtensionId,
    string Name,
    DictionaryPatch<string, string, string> Settings);
