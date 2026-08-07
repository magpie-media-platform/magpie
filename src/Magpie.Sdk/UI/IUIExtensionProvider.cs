using System.Collections.Generic;

using Magpie.Models.UI;

namespace Magpie.Sdk.UI;

/// <summary>
/// Declares UI slot definitions provided by an extension.
/// </summary>
public interface IUIExtensionProvider
{
    /// <summary>
    /// Gets the UI extension definitions provided by this extension.
    /// </summary>
    /// <returns>The list of UI extensions</returns>
    IEnumerable<UIExtension> GetUIExtensions();
}
