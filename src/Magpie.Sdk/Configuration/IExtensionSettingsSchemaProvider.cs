using System.Collections.Generic;

using Magpie.Models;

namespace Magpie.Sdk.Configuration;

/// <summary>
/// Declares extension-owned settings groups and the keys they contain.
/// </summary>
public interface IExtensionSettingsSchemaProvider
{
    /// <summary>
    /// Gets the extension-owned settings group definitions.
    /// </summary>
    /// <returns>The settings group definitions provided by this extension</returns>
    IEnumerable<ExtensionSettingsGroupDefinition> GetSettingsGroups();
}
