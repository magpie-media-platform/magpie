namespace Magpie.Models.UI;

/// <summary>
/// The rule that selects a dark or light theme.
/// </summary>
public enum ThemeMode
{
    /// <summary>
    /// Always use the dark theme.
    /// </summary>
    Dark,

    /// <summary>
    /// Always use the light theme.
    /// </summary>
    Light,

    /// <summary>
    /// Follow the device color scheme.
    /// </summary>
    System
}
