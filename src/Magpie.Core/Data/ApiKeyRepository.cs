using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Models.History;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ApiKeyEntity = Magpie.Data.Database.Entities.ApiKey;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing API keys.
/// </summary>
public sealed class ApiKeyRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<ApiKeyRepository> logger) : IApiKeyRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ApiKey>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.ApiKeys
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(k => k.ToModel()).ToImmutableList();

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
    public async Task<Result<ApiKey?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from k in context.ApiKeys.AsNoTracking()
                where k.Id == id
                select k)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting API key {ApiKeyId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<ApiKey?>("Error retrieving API key", ErrorCodes.DbApiKeyError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        ApiKeyPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await context.ApiKeys
                .FirstOrDefaultAsync(k => k.Id == patch.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            var userId = entity.UserId;

            if (patch.Enabled is not null)
            {
                entity.Enabled = patch.Enabled.Value;
            }
            if (patch.ExpiresAt is not null)
            {
                entity.ExpiresAt = patch.ExpiresAt.Value;
            }
            if (patch.LastUsedAt is not null)
            {
                entity.LastUsedAt = patch.LastUsedAt.Value;
            }

            entity.ModifiedAt = DateTimeOffset.UtcNow;
            entity.ModifiedByUserId = modifiedByUserId;

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (updated > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Security,
                    operation: "Patch",
                    entityId: patch.Id,
                    parentEntityId: userId,
                    entityType: nameof(ApiKey),
                    description: $"Updated API key {entity.Name}",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error patching API key {ApiKeyId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>("Error updating API key", ErrorCodes.DbApiKeyError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ApiKey>> CreateAsync(
        Guid? userId,
        string name,
        string keyPrefix,
        string keyHash,
        DateTimeOffset? expiresAt,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = new ApiKeyEntity
            {
                UserId = userId ?? Guid.Empty,
                Name = name,
                KeyPrefix = keyPrefix,
                KeyHash = keyHash,
                Enabled = true,
                ExpiresAt = expiresAt,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedByUserId = createdByUserId
            };

            context.ApiKeys.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Security,
                operation: "Create",
                entityId: entity.Id,
                parentEntityId: userId,
                entityType: nameof(ApiKey),
                description: $"Created API key {name}",
                userId: createdByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(entity.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error creating API key {ApiKeyName} ({ExErrorCode})",
                name,
                errorCode ?? "");
            return Result.Fail<ApiKey>("Error creating API key", ErrorCodes.DbApiKeyError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.ApiKeys
                .FirstOrDefaultAsync(k => k.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            var userId = entity.UserId;
            var name = entity.Name;

            await context.ApiKeys
                .Where(k => k.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Security,
                operation: "Delete",
                entityId: id,
                parentEntityId: userId,
                entityType: nameof(ApiKey),
                description: $"Deleted API key {name}",
                userId: deletedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(true);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error deleting API key {ApiKeyId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>("Error deleting API key", ErrorCodes.DbApiKeyError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ApiKey>> GetByKeyHashAsync(string keyHash, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from k in context.ApiKeys.AsNoTracking()
                where k.KeyHash == keyHash
                select k)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Fail<ApiKey>("invalid api key", ErrorCodes.DbApiKeyError);
            }

            return Result.Success(entity.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting API key by hash ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<ApiKey>("api key error", ErrorCodes.DbApiKeyError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ApiKey>>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from k in context.ApiKeys.AsNoTracking()
                where k.UserId == userId
                select k)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(k => k.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<ApiKey>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting API keys for user {UserId} ({ExErrorCode})",
                userId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<ApiKey>>("Error retrieving API keys", ErrorCodes.DbApiKeyError, errorCode);
        }
    }
}
