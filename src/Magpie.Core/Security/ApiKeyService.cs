using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

namespace Magpie.Core.Security;

internal sealed class ApiKeyService(
    IApiKeyRepository apiKeys,
    IPasswordHasher passwordHasher,
    IUserRepository users,
    TimeProvider timeProvider) : IApiKeyService
{
    private const string KeyPrefix = "mgp_";
    private const int KeyLength = 32;
    private const int PrefixDisplayLength = 8;

    private static string GenerateKey()
    {
        var encoded = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyLength))
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');

        return KeyPrefix + encoded;
    }

    public async Task<IReadOnlyList<ApiKey>> ListAllAsync(
        bool? enabled,
        bool? expired,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 500;
        var items = new List<ApiKey>();
        var pageNumber = 1;
        while (true)
        {
            var page = await apiKeys.QueryAsync(
                new ApiKeyQuery { Enabled = enabled, Expired = expired },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            items.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        return items;
    }

    public Task<IReadOnlyList<ApiKey>> ListOwnAsync(
        bool? enabled,
        bool? expired,
        Guid callerUserId,
        CancellationToken cancellationToken = default) =>
        ListForUserAsync(callerUserId, enabled, expired, cancellationToken);

    public async Task<IReadOnlyList<ApiKey>> ListForUserAsync(
        Guid userId,
        bool? enabled,
        bool? expired,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 500;
        var items = new List<ApiKey>();
        var pageNumber = 1;
        while (true)
        {
            var page = await apiKeys.QueryAsync(
                new ApiKeyQuery { UserId = userId, Enabled = enabled, Expired = expired },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            items.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        return items;
    }

    public async Task<ApiKey?> GetAsync(
        Guid apiKeyId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var key = await apiKeys.GetByIdAsync(apiKeyId, cancellationToken).ConfigureAwait(false);
        if (key is null || (key.UserId != callerUserId && !callerIsAdmin))
        {
            return null;
        }

        return key;
    }

    public async Task<ApiKeyWithPlaintextKey> CreateAsync(
        ApiKeyCreate create,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);

        var caller = await users.GetByIdAsync(callerUserId, cancellationToken).ConfigureAwait(false);
        if (caller is not { Enabled: true } ||
            (string.Equals(caller.Username, "system", StringComparison.Ordinal) &&
                string.Equals(caller.ProviderType, "system", StringComparison.Ordinal)))
        {
            throw new ArgumentException("A user is required when creating an API key.");
        }

        return await CreateForOwnerAsync(create, caller.Id, callerUserId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApiKeyWithPlaintextKey> CreateForUserAsync(
        ApiKeyCreate create,
        Guid callerUserId,
        string username,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var caller = await users.GetByIdAsync(callerUserId, cancellationToken).ConfigureAwait(false);
        var owners = await users.QueryAsync(
            new UserQuery { Username = username.Trim() },
            new() { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false);
        var owner = owners.Items.SingleOrDefault();

        if (caller is null || caller.Role != Role.Admin && (owner is null || owner.Id != callerUserId))
        {
            throw new UnauthorizedAccessException("Only administrators can create API keys for other users.");
        }

        if (owner is null)
        {
            throw new ArgumentException($"No user named '{username.Trim()}' was found.");
        }

        if (string.Equals(owner.Username, "system", StringComparison.Ordinal) &&
            string.Equals(owner.ProviderType, "system", StringComparison.Ordinal))
        {
            throw new ArgumentException("The system account cannot own an API key.");
        }

        if (!owner.Enabled)
        {
            throw new ArgumentException($"User '{owner.Username}' is disabled and cannot own an API key.");
        }

        return await CreateForOwnerAsync(create, owner.Id, callerUserId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ApiKeyWithPlaintextKey> CreateForOwnerAsync(
        ApiKeyCreate create,
        Guid ownerUserId,
        Guid callerUserId,
        CancellationToken cancellationToken)
    {
        var plaintextKey = GenerateKey();
        var keyPrefix = plaintextKey[..PrefixDisplayLength];
        var keyHash = await passwordHasher.HashPasswordUnsaltedAsync(plaintextKey, cancellationToken)
            .ConfigureAwait(false);

        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = ownerUserId,
            Name = create.Name,
            KeyPrefix = keyPrefix,
            KeyHash = keyHash,
            Enabled = true,
            ExpiresAt = create.ExpiresAt,
        };

        var created = await apiKeys.CreateAsync(apiKey, callerUserId, cancellationToken).ConfigureAwait(false);

        return new ApiKeyWithPlaintextKey(created, plaintextKey);
    }

    public async Task<bool> PatchAsync(
        Guid apiKeyId,
        ApiKeyPatch patch,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var key = await apiKeys.GetByIdAsync(apiKeyId, cancellationToken).ConfigureAwait(false);
        if (key is null || (key.UserId != callerUserId && !callerIsAdmin))
        {
            return false;
        }

        return await apiKeys
            .PatchAsync(patch with { Id = apiKeyId }, callerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(
        Guid apiKeyId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var key = await apiKeys.GetByIdAsync(apiKeyId, cancellationToken).ConfigureAwait(false);
        if (key is null || (key.UserId != callerUserId && !callerIsAdmin))
        {
            return false;
        }

        return await apiKeys.DeleteAsync(apiKeyId, callerUserId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApiKey?> TryValidateAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || !apiKey.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var keyHash = await passwordHasher.HashPasswordUnsaltedAsync(apiKey, cancellationToken)
            .ConfigureAwait(false);
        var key = (await apiKeys.QueryAsync(
            new ApiKeyQuery { KeyHash = keyHash },
            new() { PageNumber = 1, PageSize = 2 },
            null,
            cancellationToken).ConfigureAwait(false)).Items.SingleOrDefault();
        if (key is null ||
            !key.Enabled ||
            key.ExpiresAt is { } expiresAt && expiresAt < timeProvider.GetUtcNow().UtcDateTime)
        {
            return null;
        }

        return key;
    }
}
