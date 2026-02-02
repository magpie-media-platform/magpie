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
/// Repository for managing indexers.
/// </summary>
public sealed class IndexerRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<IndexerRepository> logger) : IIndexerRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<Indexer>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.Indexers
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing indexers");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Indexer?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Indexers
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting indexer {IndexerId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Indexer> CreateAsync(
        Indexer model,
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

                context.Indexers.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.Integrations,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(Indexer),
                    Detail = $"Created indexer {model.Name}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating indexer {IndexerName}", model.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        Indexer model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;

                var updated = await context.Indexers
                    .Where(i => i.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(i => i.FeedId, model.FeedId)
                            .SetProperty(i => i.ExtensionId, model.ExtensionId)
                            .SetProperty(i => i.ProviderType, model.ProviderType)
                            .SetProperty(i => i.Name, model.Name)
                            .SetProperty(i => i.Description, model.Description)
                            .SetProperty(i => i.Availability, model.Availability)
                            .SetProperty(i => i.Categories, model.Categories)
                            .SetProperty(i => i.Languages, model.Languages)
                            .SetProperty(i => i.ModifiedAt, now)
                            .SetProperty(i => i.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(Indexer),
                        Detail = $"Updated indexer {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating indexer {IndexerId}", model.Id);
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

                var model = await context.Indexers
                    .AsNoTracking()
                    .Where(i => i.Id == id)
                    .Select(i => new { i.Name })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                await context.IndexerConnections
                    .Where(c => c.IndexerId == id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                var deleted = await context.Indexers
                    .Where(i => i.Id == id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(Indexer),
                        Detail = $"Deleted indexer {model.Name}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting indexer {IndexerId}", id);
            throw;
        }
    }
}
