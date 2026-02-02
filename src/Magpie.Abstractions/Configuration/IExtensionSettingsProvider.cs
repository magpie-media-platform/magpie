using System;

namespace Magpie.Abstractions.Configuration;

/// <summary>
/// Provides access to extension specific settings.
/// </summary>
public interface IExtensionSettingsProvider : ISettingsProvider
{
    /// <summary>
    /// The id of the extension the settings belong to.
    /// </summary>
    Guid ExtensionId { get; }
}
