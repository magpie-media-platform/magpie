namespace Magpie.Models.Enums;

/// <summary>
/// The health status of an indexer.
/// </summary>
public enum IndexerStatus
{
    /// <summary>
    /// The indexer status is unknown.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The indexer is healthy and responding.
    /// </summary>
    Healthy,

    /// <summary>
    /// The indexer is unhealthy or not responding.
    /// </summary>
    Unhealthy,

    /// <summary>
    /// The indexer's last check is older than the required threshold.
    /// </summary>
    Stale
}
