namespace Magpie.Models.Indexers;

/// <summary>
/// The health status of an indexer.
/// </summary>
public enum IndexerStatus
{
    /// <summary>
    /// The indexer has not been checked yet.
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
    /// The indexer has not been checked for too long. The provider defines the threshold.
    /// </summary>
    Stale
}
