using System;

namespace Magpie.Models.Arguments;

/// <summary>
/// The lookup key for a settings group.
/// </summary>
public sealed record SettingsGroupLookupKey(Guid? ExtensionId, string Name);
