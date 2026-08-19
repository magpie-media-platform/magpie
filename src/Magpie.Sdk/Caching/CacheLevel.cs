namespace Magpie.Sdk.Caching;

/// <summary>
/// The storage level that a cache can use.
/// </summary>
public enum CacheLevel
{
    /// <summary>
    /// The cache is local to one process.
    /// </summary>
    Memory = 0,

    /// <summary>
    /// The cache is shared by multiple processes.
    /// </summary>
    Shared = 1,

    /// <summary>
    /// The cache remains available after a process restart.
    /// </summary>
    Durable = 2,
}
