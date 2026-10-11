namespace Magpie.Models.UI;

/// <summary>
/// A theme available for selection in the UI.
/// </summary>
public sealed record ThemeListItem
{
    /// <summary>
    /// The id scoped to the provider assembly, theme name and variant.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The display name of the theme.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The color scheme of the stylesheet.
    /// </summary>
    public ThemeVariant Variant { get; init; }
}
