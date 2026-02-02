namespace Magpie.Models.Enums;

/// <summary>
/// The health status of a download client.
/// </summary>
public enum DownloadClientStatus
{
    /// <summary>
    /// The status is unknown.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The client is healthy and responding.
    /// </summary>
    Healthy,

    /// <summary>
    /// The client is unhealthy or not responding.
    /// </summary>
    Unhealthy,

    /// <summary>
    /// The clients last check is older than the required threshold.
    /// </summary>
    Stale
}
