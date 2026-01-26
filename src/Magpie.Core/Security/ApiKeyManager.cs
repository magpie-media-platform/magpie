using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Abstractions.Security;
using Magpie.Models.Result;
using Magpie.Models.Users;

using static Magpie.Models.Result.ResultExtensions;

namespace Magpie.Core.Security;

/// <inheritdoc />
public sealed class ApiKeyManager(
    IApiKeyRepository apiKeyRepository,
    IKeyGenerator keyGenerator,
    IPasswordHasher passwordHasher) : IApiKeyManager
{
    private const string KeyPrefix = "mgp_";
    private const int KeyLength = 32;
    private const int PrefixDisplayLength = 8;

    /// <inheritdoc />
    public Task<Result<ApiKeyWithKey>> CreateApiKeyAsync(
        Guid userId,
        string name,
        DateTimeOffset? expiresAt,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var plaintextKey = keyGenerator.GenerateKey(KeyPrefix, KeyLength);
        var keyPrefix = plaintextKey[..PrefixDisplayLength];

        return passwordHasher.HashPasswordUnsaltedAsync(plaintextKey, cancellationToken)
            .BindAsync(keyHash => apiKeyRepository
                .CreateAsync(
                    userId,
                    name,
                    keyPrefix,
                    keyHash,
                    expiresAt,
                    createdByUserId,
                    cancellationToken))
            .MapAsync(apiKey => new ApiKeyWithKey(apiKey, plaintextKey));
    }

    /// <inheritdoc />
    public Task<Result<ApiKeyWithKey>> RotateApiKeyAsync(
        Guid id,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        var plaintextKey = keyGenerator.GenerateKey(KeyPrefix, KeyLength);
        var keyPrefix = plaintextKey[..PrefixDisplayLength];

        return apiKeyRepository.GetByIdAsync(id, cancellationToken)
            .BindAsync(existing => existing is not null
                ? Result.Success(existing)
                : Result.Fail<ApiKey>("invalid api key", ErrorCodes.ApiKeyNotFound))
            .ZipAsync(passwordHasher.HashPasswordUnsaltedAsync(plaintextKey, cancellationToken))
            .BindAsync((existing, keyHash) =>
                apiKeyRepository
                    .DeleteAsync(id, modifiedByUserId, cancellationToken)
                    .BindAsync(_ => apiKeyRepository.CreateAsync(
                        existing.UserId,
                        existing.Name,
                        keyPrefix,
                        keyHash,
                        existing.ExpiresAt,
                        modifiedByUserId,
                        cancellationToken))
                    .MapAsync(apiKey => new ApiKeyWithKey(apiKey, plaintextKey)));
    }

    /// <inheritdoc />
    public Task<Result<IEnumerable<ApiKey>>> GetApiKeysAsync(
        Guid? userId = null,
        bool? enabled = null,
        bool? expired = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        return (userId.HasValue
                ? apiKeyRepository.GetByUserIdAsync(userId.Value, cancellationToken)
                : apiKeyRepository.GetAllAsync(cancellationToken))
            .MapAsync(keys => enabled is null
                ? keys
                : keys.Where(k => k.Enabled == enabled.Value))
            .MapAsync(keys => expired is null
                ? keys
                : keys.Where(k => k.ExpiresAt <= now));
    }

    /// <inheritdoc />
    public Task<Result<ApiKey?>> GetApiKeyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return apiKeyRepository.GetByIdAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<ApiKey>> EnableApiKeyAsync(
        Guid id,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        return apiKeyRepository.PatchAsync(new() { Id = id, Enabled = new(true) }, modifiedByUserId, cancellationToken)
            .BindAsync(async patched => patched
                ? await apiKeyRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
                : Result.Fail<ApiKey?>("API key not found", ErrorCodes.ApiKeyNotFound))
            .BindAsync(apiKey => apiKey is not null
                ? Result.Success(apiKey)
                : Result.Fail<ApiKey>("API key not found", ErrorCodes.ApiKeyNotFound));
    }

    /// <inheritdoc />
    public Task<Result<ApiKey>> DisableApiKeyAsync(
        Guid id,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        return apiKeyRepository.PatchAsync(new() { Id = id, Enabled = new(false) }, modifiedByUserId, cancellationToken)
            .BindAsync(async patched => patched
                ? await apiKeyRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
                : Result.Fail<ApiKey?>("invalid api key", ErrorCodes.ApiKeyNotFound))
            .BindAsync(apiKey => apiKey is not null
                ? Result.Success(apiKey)
                : Result.Fail<ApiKey>("invalid api key", ErrorCodes.ApiKeyNotFound));
    }

    /// <inheritdoc />
    public Task<Result<bool>> DeleteApiKeyAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default)
    {
        return apiKeyRepository.DeleteAsync(id, deletedByUserId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<ApiKey>> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || !apiKey.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return Result.FailAsync<ApiKey>("invalid api key", ErrorCodes.ApiKeyInvalid);
        }

        return passwordHasher.HashPasswordUnsaltedAsync(apiKey, cancellationToken)
            .BindAsync(keyHash => apiKeyRepository.GetByKeyHashAsync(keyHash, cancellationToken))
            .BindAsync(key => key switch
            {
                { Enabled: false } =>
                    Result.Fail<ApiKey>("invalid api key", ErrorCodes.ApiKeyDisabled),
                { ExpiresAt: { } exp } when exp < DateTimeOffset.UtcNow =>
                    Result.Fail<ApiKey>("invalid api key", ErrorCodes.ApiKeyExpired),
                _ =>
                    Result.Success(key)
            });
    }
}
