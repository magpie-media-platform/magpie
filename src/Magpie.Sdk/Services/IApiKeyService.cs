using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for API keys.
/// </summary>
public interface IApiKeyService
{
    /// <summary>
    /// Lists the API keys of every user.
    /// </summary>
    /// <param name="enabled">The enabled state to match, or <see langword="null"/> for every key</param>
    /// <param name="expired">The expiry state to match, or <see langword="null"/> for every key</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<ApiKey>> ListAllAsync(
        bool? enabled,
        bool? expired,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the API keys of the calling user.
    /// </summary>
    /// <param name="enabled">The enabled state to match, or <see langword="null"/> for every key</param>
    /// <param name="expired">The expiry state to match, or <see langword="null"/> for every key</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<ApiKey>> ListOwnAsync(
        bool? enabled,
        bool? expired,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the API keys of one user.
    /// </summary>
    /// <param name="userId">The id of the user that owns the keys</param>
    /// <param name="enabled">The enabled state to match, or <see langword="null"/> for every key</param>
    /// <param name="expired">The expiry state to match, or <see langword="null"/> for every key</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<ApiKey>> ListForUserAsync(
        Guid userId,
        bool? enabled,
        bool? expired,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an API key by id when the caller owns it or is an administrator.
    /// </summary>
    /// <param name="apiKeyId">The id of the API key to return</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ApiKey?> GetAsync(
        Guid apiKeyId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an API key for the calling user and returns its one-time plaintext value.
    /// </summary>
    /// <param name="create">The values for the new API key</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ApiKeyWithPlaintextKey> CreateAsync(
        ApiKeyCreate create,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an API key for an enabled user selected by username.
    /// </summary>
    /// <param name="create">The values for the new API key</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="username">The username of the user that owns the new key</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ApiKeyWithPlaintextKey> CreateForUserAsync(
        ApiKeyCreate create,
        Guid callerUserId,
        string username,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches an API key when the caller owns it or is an administrator.
    /// </summary>
    /// <param name="apiKeyId">The id of the API key to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchAsync(
        Guid apiKeyId,
        ApiKeyPatch patch,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an API key when the caller owns it or is an administrator.
    /// </summary>
    /// <param name="apiKeyId">The id of the API key to delete</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteAsync(
        Guid apiKeyId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a plaintext API key for authentication.
    /// </summary>
    /// <param name="apiKey">The plaintext API key from the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ApiKey?> TryValidateAsync(
        string apiKey,
        CancellationToken cancellationToken = default);
}
