using System;

namespace Magpie.Models.Configuration;

/// <summary>
/// A group of related settings.
/// </summary>
/// <param name="Id">The settings group identifier.</param>
/// <param name="Name">The settings group name.</param>
/// <param name="ExtensionId">The extension that owns this group, or null if not extension-specific.</param>
public sealed record SettingsGroup(
    Guid Id,
    string Name,
    Guid? ExtensionId);
