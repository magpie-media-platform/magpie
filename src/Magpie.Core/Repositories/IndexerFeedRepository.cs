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
/// Repository for managing indexer feed configurations.
/// </summary>
public sealed class IndexerFeedRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<IndexerFeedRepository> logger) : IIndexerFeedRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<IndexerFeed>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.IndexerFeeds
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing indexer feeds");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IndexerFeed?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.IndexerFeeds
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting indexer feed {IndexerFeedId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IndexerFeed> CreateAsync(
        IndexerFeed model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                model.CreatedAt = now;
                model.CreatedByUserId = createdByUserId;

                context.IndexerFeeds.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.Integrations,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(IndexerFeed),
                    Detail = $"Created indexer feed {model.Name}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating indexer feed {IndexerFeedName}", model.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        IndexerFeed model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;

                var updated = await context.IndexerFeeds
                    .Where(f => f.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(f => f.ExtensionId, model.ExtensionId)
                            .SetProperty(f => f.ProviderType, model.ProviderType)
                            .SetProperty(f => f.Name, model.Name)
                            .SetProperty(f => f.Description, model.Description)
                            .SetProperty(f => f.Settings, model.Settings)
                            .SetProperty(f => f.Enabled, model.Enabled)
                            .SetProperty(f => f.LastSync, model.LastSync)
                            .SetProperty(f => f.ModifiedAt, now)
                            .SetProperty(f => f.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(IndexerFeed),
                        Detail = $"Updated indexer feed {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating indexer feed {IndexerFeedId}", model.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;

                var model = await context.IndexerFeeds
                    .AsNoTracking()
                    .Where(f => f.Id == id)
                    .Select(f => new { f.Name })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var deleted = await context.IndexerFeeds
                    .Where(f => f.Id == id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(IndexerFeed),
                        Detail = $"Deleted indexer feed {model.Name}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting indexer feed {IndexerFeedId}", id);
            throw;
        }
    }
}
