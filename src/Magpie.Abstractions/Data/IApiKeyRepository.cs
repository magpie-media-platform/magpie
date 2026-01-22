using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;
using Magpie.Models.Users;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing API keys.
/// </summary>
public interface IApiKeyRepository : IPatchRepository<Guid, ApiKey, ApiKeyPatch>
{
    /// <summary>
    /// Get an API key by key prefix and hash.
    /// </summary>
    /// <param name="keyPrefix">The API key prefix.</param>
    /// <param name="keyHash">The hashed API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<ApiKey>> GetByPrefixAndHashAsync(
        string keyPrefix,
        string keyHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all API keys for a user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<IReadOnlyList<ApiKey>>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new API key.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="name">A user-friendly name for identifying this key.</param>
    /// <param name="keyPrefix">The first few characters of the key for identification.</param>
    /// <param name="keyHash">The hashed API key.</param>
    /// <param name="expiresAt">When the API key expires, if set.</param>
    /// <param name="createdByUserId">The user who is creating the API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<ApiKey>> CreateAsync(
        Guid userId,
        string name,
        string keyPrefix,
        string keyHash,
        DateTimeOffset? expiresAt,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an API key by ID.
    /// </summary>
    /// <param name="id">The API key identifier.</param>
    /// <param name="deletedByUserId">The user who is deleting this API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<bool>> DeleteAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Record API key usage.
    /// </summary>
    /// <param name="id">The API key identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> RecordUsageAsync(Guid id, CancellationToken cancellationToken = default);
}
