using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing API keys.
/// </summary>
public interface IApiKeyRepository : ICrudRepository<Guid, ApiKey>
{
    /// <summary>
    /// Get all API keys for a user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<ApiKey>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get an API key by its hashed value.
    /// </summary>
    /// <param name="keyHash">The hashed API key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<ApiKey?> GetByKeyHashAsync(string keyHash, CancellationToken cancellationToken = default);
}
