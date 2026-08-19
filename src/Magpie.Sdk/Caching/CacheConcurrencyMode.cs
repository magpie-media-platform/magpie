namespace Magpie.Sdk.Caching;

/// <summary>
/// The behavior for concurrent cache population.
/// </summary>
public enum CacheConcurrencyMode
{
    /// <summary>
    /// Concurrent callers can populate one key at the same time.
    /// </summary>
    Optimistic = 0,

    /// <summary>
    /// Concurrent callers share one population operation for each key.
    /// </summary>
    Pessimistic = 1,
}
