using System.Collections.Generic;

using Magpie.Models.UI;

namespace Magpie.Sdk.UI;

/// <summary>
/// Supplies theme stylesheets embedded in the provider assembly.
/// </summary>
public interface IThemeProvider
{
    /// <summary>
    /// Lists the named dark and light theme variants.
    /// </summary>
    IEnumerable<ThemeDefinition> GetThemes();
}
