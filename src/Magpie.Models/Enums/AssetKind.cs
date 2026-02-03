namespace Magpie.Models.Enums;

/// <summary>
/// Types of UI assets provided by extensions.
/// </summary>
public enum AssetKind
{
    /// <summary>
    /// A JavaScript file loaded via script tag.
    /// </summary>
    Script = 0,

    /// <summary>
    /// A CSS stylesheet loaded via link tag.
    /// </summary>
    Style,

    /// <summary>
    /// A link element with custom rel attribute (preconnect, icon, prefetch, etc.).
    /// </summary>
    Link
}
