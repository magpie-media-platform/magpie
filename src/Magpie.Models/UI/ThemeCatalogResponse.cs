using System.Collections.Generic;

namespace Magpie.Models.UI;

/// <summary>
/// The available themes and instance theme defaults.
/// </summary>
public sealed record ThemeCatalogResponse
{
    /// <summary>
    /// The available theme variants.
    /// </summary>
    public required IReadOnlyList<ThemeListItem> Themes { get; init; }

    /// <summary>
    /// The theme settings used when a browser has no override.
    /// </summary>
    public required ThemeSettings Defaults { get; init; }
}
