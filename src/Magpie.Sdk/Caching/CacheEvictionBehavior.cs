namespace Magpie.Sdk.Caching;

/// <summary>
/// The behavior that removes cache entries when the cache is full.
/// </summary>
public enum CacheEvictionBehavior
{
    /// <summary>
    /// Remove entries that the caller used least recently.
    /// </summary>
    LeastRecentlyUsed = 0,
}
