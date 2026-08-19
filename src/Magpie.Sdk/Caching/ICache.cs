using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Sdk.Caching;

/// <summary>
/// A general cache that stores values for one caller policy.
/// </summary>
public interface ICache
{
    /// <summary>
    /// Gets a cached value.
    /// </summary>
    /// <typeparam name="T">The type of the cached value</typeparam>
    /// <param name="key">The stable cache key</param>
    /// <param name="value">The cached value when the method returns <see langword="true"/></param>
    /// <returns><see langword="true"/> when a fresh value exists.</returns>
    bool TryGet<T>(string key, out T? value);

    /// <summary>
    /// Gets a value or creates and stores it when the key is not cached.
    /// </summary>
    /// <typeparam name="T">The type of the cached value</typeparam>
    /// <param name="key">The stable cache key</param>
    /// <param name="valueFactory">The factory for a missing value</param>
    /// <param name="cancellationToken">The token that cancels this operation</param>
    /// <returns>The cached or created value.</returns>
    ValueTask<T> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> valueFactory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a cached value.
    /// </summary>
    /// <param name="key">The stable cache key</param>
    /// <returns><see langword="true"/> when a value was removed.</returns>
    bool Remove(string key);

    /// <summary>
    /// Stores a value in the cache.
    /// </summary>
    /// <typeparam name="T">The type of the cached value</typeparam>
    /// <param name="key">The stable cache key</param>
    /// <param name="value">The value to store</param>
    void Set<T>(string key, T value);
}
