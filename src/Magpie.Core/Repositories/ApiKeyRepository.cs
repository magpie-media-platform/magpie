using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

/// <summary>
/// Repository for managing API keys.
/// </summary>
public sealed class ApiKeyRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<ApiKeyRepository> logger) : IApiKeyRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<ApiKey>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.ApiKeys
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing API keys");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.ApiKeys
                .AsNoTracking()
                .FirstOrDefaultAsync(k => k.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting API key {ApiKeyId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ApiKey> CreateAsync(
        ApiKey model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                model.CreatedAt = DateTime.UtcNow;
                model.CreatedByUserId = createdByUserId;

                context.ApiKeys.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.Security,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    ParentEntityId = model.UserId.ToString(),
                    EntityType = nameof(ApiKey),
                    Detail = $"Created API key {model.Name}",
                    Timestamp = DateTime.UtcNow,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating API key {ApiKeyName}", model.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(ApiKey model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;

                var updated = await context.ApiKeys
                    .Where(k => k.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(k => k.Name, model.Name)
                            .SetProperty(k => k.Enabled, model.Enabled)
                            .SetProperty(k => k.ExpiresAt, model.ExpiresAt)
                            .SetProperty(k => k.LastUsedAt, model.LastUsedAt)
                            .SetProperty(k => k.ModifiedAt, now)
                            .SetProperty(k => k.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Security,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        ParentEntityId = model.UserId.ToString(),
                        EntityType = nameof(ApiKey),
                        Detail = $"Updated API key {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }
                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating API key {ApiKeyId}", model.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var model = await context.ApiKeys
                    .AsNoTracking()
                    .Where(k => k.Id == id)
                    .Select(k => new { k.UserId, k.Name })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var deleted = await context.ApiKeys
                    .Where(k => k.Id == id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Security,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        ParentEntityId = model.UserId.ToString(),
                        EntityType = nameof(ApiKey),
                        Detail = $"Deleted API key {model.Name}",
                        Timestamp = DateTime.UtcNow,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }
                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting API key {ApiKeyId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ApiKey>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            return await context.ApiKeys
                .AsNoTracking()
                .Where(k => k.UserId == userId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting API keys for user {UserId}", userId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ApiKey?> GetByKeyHashAsync(string keyHash, CancellationToken cancellationToken)
    {
        try
        {
            return await context.ApiKeys
                .AsNoTracking()
                .FirstOrDefaultAsync(k => k.KeyHash == keyHash, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting API key by hash");
            throw;
        }
    }
}
