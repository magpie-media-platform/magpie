namespace Magpie.Models.Enums;

/// <summary>
/// The monitoring intent for a content item.
/// </summary>
public enum MonitoringIntent
{
    /// <summary>
    /// The content item is not monitored.
    /// </summary>
    None = 0,

    /// <summary>
    /// The content item is actively monitored for releases.
    /// </summary>
    Active,

    /// <summary>
    /// The content item is excluded from monitoring.
    /// </summary>
    Excluded
}
