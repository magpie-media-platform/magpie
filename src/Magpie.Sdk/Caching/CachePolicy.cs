using System;

namespace Magpie.Sdk.Caching;

/// <summary>
/// The performance and retention requirements for a cache.
/// </summary>
public sealed record CachePolicy
{
    /// <summary>
    /// The highest storage level that the caller accepts.
    /// </summary>
    public CacheLevel MaximumLevel { get; set; } = CacheLevel.Memory;

    /// <summary>
    /// The maximum age of a value that a caller can use.
    /// </summary>
    public TimeSpan Freshness { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The maximum time that the cache retains a value.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The maximum number of entries that the cache retains.
    /// </summary>
    public int MaximumEntries { get; set; } = 1000;

    /// <summary>
    /// The behavior that removes entries when the cache is full.
    /// </summary>
    public CacheEvictionBehavior EvictionBehavior { get; set; } = CacheEvictionBehavior.LeastRecentlyUsed;

    /// <summary>
    /// The behavior for concurrent cache population.
    /// </summary>
    public CacheConcurrencyMode ConcurrencyMode { get; set; } = CacheConcurrencyMode.Optimistic;
}
