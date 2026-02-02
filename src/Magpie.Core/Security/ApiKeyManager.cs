using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models.Data;

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
    public async Task<ApiKeyWithPlaintextKey> CreateApiKeyAsync(
        Guid userId,
        string name,
        DateTime? expiresAt,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var plaintextKey = keyGenerator.GenerateKey(KeyPrefix, KeyLength);
        var keyPrefix = plaintextKey[..PrefixDisplayLength];
        var keyHash = await passwordHasher.HashPasswordUnsaltedAsync(plaintextKey, cancellationToken)
            .ConfigureAwait(false);

        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            KeyPrefix = keyPrefix,
            KeyHash = keyHash,
            Enabled = true,
            ExpiresAt = expiresAt,
        };

        var created = await apiKeyRepository.CreateAsync(apiKey, createdByUserId, cancellationToken)
            .ConfigureAwait(false);

        return new ApiKeyWithPlaintextKey(created, plaintextKey);
    }

    /// <inheritdoc />
    public async Task<ApiKeyWithPlaintextKey> RotateApiKeyAsync(
        Guid id,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        var existing = await apiKeyRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("API key not found");

        var plaintextKey = keyGenerator.GenerateKey(KeyPrefix, KeyLength);
        var keyPrefix = plaintextKey[..PrefixDisplayLength];
        var keyHash = await passwordHasher.HashPasswordUnsaltedAsync(plaintextKey, cancellationToken)
            .ConfigureAwait(false);

        await apiKeyRepository.DeleteAsync(id, modifiedByUserId, cancellationToken).ConfigureAwait(false);

        var newApiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = existing.UserId,
            Name = existing.Name,
            KeyPrefix = keyPrefix,
            KeyHash = keyHash,
            Enabled = true,
            ExpiresAt = existing.ExpiresAt,
        };

        var created = await apiKeyRepository.CreateAsync(newApiKey, modifiedByUserId, cancellationToken)
            .ConfigureAwait(false);

        return new ApiKeyWithPlaintextKey(created, plaintextKey);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ApiKey>> ListApiKeysAsync(
        Guid? userId = null,
        bool? enabled = null,
        bool? expired = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var keys = userId.HasValue
            ? await apiKeyRepository.GetByUserIdAsync(userId.Value, cancellationToken).ConfigureAwait(false)
            : await apiKeyRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);

        if (enabled.HasValue)
        {
            keys = keys.Where(k => k.Enabled == enabled.Value);
        }

        if (expired.HasValue)
        {
            keys = keys.Where(k => k.ExpiresAt <= now);
        }

        return keys;
    }

    /// <inheritdoc />
    public Task<ApiKey?> GetApiKeyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return apiKeyRepository.GetByIdAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ApiKey> EnableApiKeyAsync(
        Guid id,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        var apiKey = await apiKeyRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("API key not found");

        apiKey = apiKey with { Enabled = true };
        await apiKeyRepository.UpdateAsync(apiKey, modifiedByUserId, cancellationToken).ConfigureAwait(false);

        return apiKey;
    }

    /// <inheritdoc />
    public async Task<ApiKey> DisableApiKeyAsync(
        Guid id,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        var apiKey = await apiKeyRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("API key not found");

        apiKey = apiKey with { Enabled = false };
        await apiKeyRepository.UpdateAsync(apiKey, modifiedByUserId, cancellationToken).ConfigureAwait(false);

        return apiKey;
    }

    /// <inheritdoc />
    public Task<bool> DeleteApiKeyAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default)
    {
        return apiKeyRepository.DeleteAsync(id, deletedByUserId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ApiKey?> TryValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || !apiKey.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var keyHash = await passwordHasher.HashPasswordUnsaltedAsync(apiKey, cancellationToken)
            .ConfigureAwait(false);

        var key = await apiKeyRepository.GetByKeyHashAsync(keyHash, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("API key not found");

        if (!key.Enabled)
        {
            throw new InvalidOperationException("API key is disabled");
        }

        if (key.ExpiresAt.HasValue && key.ExpiresAt.Value < DateTime.UtcNow)
        {
            throw new InvalidOperationException("API key has expired");
        }

        return key;
    }
}
