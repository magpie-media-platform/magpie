using System;

namespace Magpie.Sdk.Caching;

/// <summary>
/// Creates general caches for caller policies.
/// </summary>
public interface ICacheFactory
{
    /// <summary>
    /// Gets one named cache that is shared across dependency-injection scopes.
    /// </summary>
    /// <param name="name">The stable cache name</param>
    /// <param name="createPolicy">The policy factory to use when the named cache does not exist</param>
    /// <returns>The shared named cache.</returns>
    ICache GetOrCreate(string name, Func<CachePolicy>? createPolicy = null);

    /// <summary>
    /// Creates a private cache that lives for the current dependency-injection scope.
    /// </summary>
    /// <param name="name">The logical cache name</param>
    /// <param name="policy">The cache policy, or <see langword="null"/> for the default policy</param>
    /// <returns>The private cache.</returns>
    ICache CreateScoped(string name, CachePolicy? policy = null);
}
