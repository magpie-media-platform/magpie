namespace Magpie.Models.Enums;

/// <summary>
/// Defines how a library import initializes monitoring.
/// </summary>
public enum LibraryImportMonitoringMode
{
    /// <summary>
    /// Uses inherited monitoring for the imported top-level item.
    /// </summary>
    Inherit = 0,

    /// <summary>
    /// Monitors the imported top-level item and its inheriting descendants.
    /// </summary>
    Monitored = 1,

    /// <summary>
    /// Does not monitor the imported top-level item or its inheriting descendants.
    /// </summary>
    Unmonitored = 2,

    /// <summary>
    /// Monitors the top-level item and future-dated descendants only.
    /// </summary>
    Future = 3,
}
