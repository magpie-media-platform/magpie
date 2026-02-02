using System;

using Magpie.Models.Data;

namespace Magpie.Models.Arguments;

/// <summary>
/// Lookup key for a settings group in a given extension.
/// </summary>
/// <param name="ExtensionId">Lookup by <see cref="SettingsGroup.Id"/> </param>
/// <param name="Name">Lookup by <see cref="SettingsGroup.Name"/></param>
public sealed record SettingsGroupLookupKey(Guid? ExtensionId, string Name);
