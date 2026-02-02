namespace Magpie.Models.Enums;

/// <summary>
/// The health status of a media server.
/// </summary>
public enum MediaServerStatus
{
    /// <summary>
    /// The server has not been checked yet.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The server is healthy and responding.
    /// </summary>
    Healthy,

    /// <summary>
    /// The server is unhealthy or not responding.
    /// </summary>
    Unhealthy,

    /// <summary>
    /// The server has not been checked for too long.
    /// </summary>
    Stale
}
