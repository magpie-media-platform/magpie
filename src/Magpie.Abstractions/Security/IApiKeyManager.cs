using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;
using Magpie.Models.Users;

namespace Magpie.Abstractions.Security;

/// <summary>
/// Provides API key management services including creation, validation, and lifecycle management.
/// </summary>
public interface IApiKeyManager
{
    /// <summary>
    /// Creates a new API key for a user.
    /// </summary>
    /// <param name="userId">The id of the user the API key is associated with.</param>
    /// <param name="name">A user-friendly name for identifying this key.</param>
    /// <param name="expiresAt">When the API key expires, if set.</param>
    /// <param name="createdByUserId">The id of the user creating the API key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created API key with its plaintext value. The plaintext is only returned once.</returns>
    Task<Result<ApiKeyWithKey>> CreateApiKeyAsync(
        Guid userId,
        string name,
        DateTimeOffset? expiresAt,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces an existing API key.
    /// </summary>
    /// <param name="id">The API key id.</param>
    /// <param name="modifiedByUserId">The id of the user rotating the API key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<ApiKeyWithKey>> RotateApiKeyAsync(
        Guid id,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get API keys.
    /// </summary>
    /// <param name="userId">Filter api keys by userId.</param>
    /// <param name="enabled">Filter api keys by enabled status.</param>
    /// <param name="expired">Filter api keys by expired status.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IEnumerable<ApiKey>>> GetApiKeysAsync(
        Guid? userId = null,
        bool? enabled = null,
        bool? expired = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an API key is enabled and not expired.
    /// </summary>
    /// <param name="apiKey">The plaintext API key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The API key if valid.</returns>
    Task<Result<ApiKey>> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an API key by id.
    /// </summary>
    /// <param name="id">The API key id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<ApiKey?>> GetApiKeyAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enable an API key.
    /// </summary>
    Task<Result<ApiKey>> EnableApiKeyAsync(
        Guid id,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disable an API key.
    /// </summary>
    /// <param name="id">The API key id.</param>
    /// <param name="modifiedByUserId">The id of the user disabling the key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<ApiKey>> DisableApiKeyAsync(
        Guid id,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an API key.
    /// </summary>
    /// <param name="id">The API key identifier.</param>
    /// <param name="deletedByUserId">The id of the user deleting the API key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the API key was deleted, <see langword="false"/> otherwise.</returns>
    Task<Result<bool>> DeleteApiKeyAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken = default);
}
