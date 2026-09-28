namespace Magpie.Models.Enums;

/// <summary>
/// How automation acquires releases for a content hierarchy item.
/// </summary>
public enum MonitoringMode
{
    /// <summary>
    /// Automation does not acquire releases for the item.
    /// </summary>
    Unmonitored = 0,

    /// <summary>
    /// Automation acquires the best acceptable release for the item.
    /// </summary>
    Monitored = 1,

    /// <summary>
    /// Automation grabs every acceptable release for the item at the same time.
    /// </summary>
    Shotgun = 2,
}
