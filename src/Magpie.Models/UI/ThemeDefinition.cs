namespace Magpie.Models.UI;

/// <summary>
/// A named theme variant that an extension supplies.
/// </summary>
public sealed record ThemeDefinition
{
    /// <summary>
    /// The display name, shared by the dark and light variants of the same theme.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The exact embedded resource name of the CSS file in the provider assembly.
    /// </summary>
    public required string FileName { get; set; }

    /// <summary>
    /// The color scheme used for base tokens before the stylesheet overrides them.
    /// </summary>
    public ThemeVariant Variant { get; set; }
}
