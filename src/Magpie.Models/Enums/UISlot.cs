namespace Magpie.Models.Enums;

/// <summary>
/// Predefined locations where extensions can inject UI components.
/// </summary>
public enum UISlot
{
    /// <summary>
    /// Global header area.
    /// </summary>
    Header = 0,

    /// <summary>
    /// Global sidebar area.
    /// </summary>
    Sidebar,

    /// <summary>
    /// Global footer area.
    /// </summary>
    Footer,

    /// <summary>
    /// Dashboard page main content area.
    /// </summary>
    DashboardMain,

    /// <summary>
    /// Content details page header area.
    /// </summary>
    ContentDetailsHeader,

    /// <summary>
    /// Content details page actions area.
    /// </summary>
    ContentDetailsActions,

    /// <summary>
    /// Settings page main content area.
    /// </summary>
    SettingsMain,

    /// <summary>
    /// New tab in main navigation.
    /// </summary>
    MainTab,

    /// <summary>
    /// New tab in settings page.
    /// </summary>
    SettingsTab,

    /// <summary>
    /// Modal or overlay area.
    /// </summary>
    Overlay
}
