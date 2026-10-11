namespace Magpie.Models.UI;

/// <summary>
/// The instance theme defaults.
/// </summary>
public sealed record ThemeSettings
{
    /// <summary>
    /// The rule that selects the active variant.
    /// </summary>
    public ThemeMode Mode { get; set; }

    /// <summary>
    /// The catalog id of the dark theme.
    /// </summary>
    public required string DarkTheme { get; set; }

    /// <summary>
    /// The catalog id of the light theme.
    /// </summary>
    public required string LightTheme { get; set; }
}
