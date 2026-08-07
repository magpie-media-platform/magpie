namespace Magpie.Models.Enums;

/// <summary>
/// The kind of a UI asset that an extension supplies.
/// </summary>
public enum AssetKind
{
    /// <summary>
    /// A JavaScript file that the page loads with a script tag.
    /// </summary>
    Script = 0,

    /// <summary>
    /// A CSS stylesheet that the page loads with a link tag.
    /// </summary>
    Style,

    /// <summary>
    /// A link element with a custom rel attribute, such as preconnect, icon, or prefetch.
    /// </summary>
    Link
}
