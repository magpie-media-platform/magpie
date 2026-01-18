namespace Magpie.Models.Downloads;

/// <summary>
/// The health status of a download client.
/// </summary>
public enum DownloadClientStatus
{
    /// <summary>
    /// The client has not been checked yet.
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
    /// The client has not been checked for too long.
    /// </summary>
    Stale
}
