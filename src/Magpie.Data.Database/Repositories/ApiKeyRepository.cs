using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Data.Database.Repositories;

/// <summary>
/// Repository for managing API keys.
/// </summary>
public sealed class ApiKeyRepository(MagpieDbContext context, ILogger<ApiKeyRepository> logger) : IApiKeyRepository
{
    /// <inheritdoc />
    public async Task<Result<ApiKey?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var apiKey = await (
                from a in context.ApiKeys
                where a.Id == id
                select a)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(apiKey?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting API key {ApiKeyId} ({ExErrorCode})", id, errorCode ?? "");
            return Result.Fail<ApiKey?>("Error retrieving API key", ErrorCodes.DbApiKeyError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ApiKey>> GetByKeyAsync(
        string keyPrefix,
        string keyHash,
        CancellationToken cancellationToken)
    {
        try
        {
            var apiKey = await (
                from a in context.ApiKeys
                where a.KeyPrefix == keyPrefix
                where a.KeyHash == keyHash
                select a)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (apiKey == null)
            {
                return Result.Fail<ApiKey>("API key not found", ErrorCodes.DbApiKeyError);
            }

            return Result.Success(apiKey.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting API key {KeyPrefix} ({ExErrorCode})", keyPrefix, errorCode ?? "");
            return Result.Fail<ApiKey>("Error retrieving API key", ErrorCodes.DbApiKeyError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ApiKey>>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var apiKeys = await (
                from a in context.ApiKeys
                where a.UserId == userId
                select a)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = apiKeys.Select(a => a.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<ApiKey>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting API keys for user {UserId} ({ExErrorCode})", userId, errorCode ?? "");
            return Result.Fail<IReadOnlyList<ApiKey>>("Error retrieving API keys", ErrorCodes.DbApiKeyError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ApiKey>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var apiKeys = await (
                from a in context.ApiKeys
                select a).ToListAsync(cancellationToken).ConfigureAwait(false);

            var result = apiKeys.Select(a => a.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<ApiKey>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting API keys ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<ApiKey>>("Error retrieving API keys", ErrorCodes.DbApiKeyError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ApiKey>> CreateAsync(
        ApiKey model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await context.Users.FindAsync([model.UserId], cancellationToken).ConfigureAwait(false);
            if (user == null)
            {
                return Result.Fail<ApiKey>("User not found.", ErrorCodes.DbUserError);
            }

            var entity = model.ToEntity(user, createdByUserId);

            context.ApiKeys.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(entity.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error creating API key {ApiKeyName} for user {UserId} ({ExErrorCode})", model.Name, model.UserId, errorCode ?? "");
            return Result.Fail<ApiKey>("Error creating API key", ErrorCodes.DbApiKeyError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        ApiKey model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.ApiKeys
                .Where(a => a.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Name, model.Name)
                        .SetProperty(e => e.Enabled, model.Enabled)
                        .SetProperty(e => e.ExpiresAt, model.ExpiresAt)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error updating API key {ApiKeyId} ({ExErrorCode})", model.Id, errorCode ?? "");
            return Result.Fail<bool>("Error saving API key", ErrorCodes.DbApiKeyError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        ApiKeyPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.ApiKeys
                .Where(a => a.Id == patch.Id)
                .ExecuteUpdateAsync(setter =>
                {
                    if (patch.Name != null)
                    {
                        setter.SetProperty(e => e.Name, patch.Name.Value!);
                    }
                    if (patch.Enabled != null)
                    {
                        setter.SetProperty(e => e.Enabled, patch.Enabled.Value);
                    }
                    if (patch.ExpiresAt != null)
                    {
                        setter.SetProperty(e => e.ExpiresAt, patch.ExpiresAt.Value);
                    }
                    setter.SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow);
                    setter.SetProperty(e => e.ModifiedByUserId, modifiedByUserId);
                }, cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error patching API key {ApiKeyId} ({ExErrorCode})", patch.Id, errorCode ?? "");
            return Result.Fail<bool>("Error saving API key", ErrorCodes.DbApiKeyError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result> RecordUsageAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.ApiKeys
                .Where(a => a.Id == id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.LastUsedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error recording usage for API key {ApiKeyId} ({ExErrorCode})", id, errorCode ?? "");
            return Result.Fail("Error recording usage", ErrorCodes.DbApiKeyError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await context.ApiKeys
                .Where(a => a.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(deleted > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error deleting API key {ApiKeyId} ({ExErrorCode})", id, errorCode ?? "");
            return Result.Fail<bool>("Error deleting API key", ErrorCodes.DbApiKeyError, errorCode);
        }
    }
}
