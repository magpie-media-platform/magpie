using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="SettingsGroup"/>.
/// </summary>
/// <param name="ExtensionId">The <see cref="SettingsGroup.ExtensionId"/> of the item to patch.</param>
/// <param name="Name">The <see cref="SettingsGroup.Name"/> of the item to patch.</param>
/// <param name="Settings">The <see cref="SettingsGroup.Settings"/> of the item to patch.</param>
public sealed record SettingsGroupPatch(
    Guid? ExtensionId,
    string Name,
    DictionaryPatch<string, string, string> Settings);
