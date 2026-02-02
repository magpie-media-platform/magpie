using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

/// <summary>
/// Repository for managing indexer connections.
/// </summary>
public sealed class IndexerConnectionRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<IndexerConnectionRepository> logger) : IIndexerConnectionRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<IndexerConnection>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.IndexerConnections
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing indexer connections");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IndexerConnection?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.IndexerConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting indexer connection {IndexerConnectionId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<IndexerConnection>> ListByIndexerIdAsync(
        Guid indexerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.IndexerConnections
                .AsNoTracking()
                .Where(c => c.IndexerId == indexerId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing indexer connections for indexer {IndexerId}", indexerId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<IndexerConnection>> ListByTagAsync(
        string tagName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.Tags
                .AsNoTracking()
                .Where(t => t.Name == tagName)
                .SelectMany(t => t.IndexerConnections)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing indexer connections by tag {TagName}", tagName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IndexerConnection> CreateAsync(
        IndexerConnection model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                model.CreatedAt = now;
                model.CreatedByUserId = createdByUserId;

                context.IndexerConnections.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.Integrations,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(IndexerConnection),
                    Detail = $"Created indexer connection {model.Name}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating indexer connection {IndexerConnectionName}", model.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        IndexerConnection model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;

                var updated = await context.IndexerConnections
                    .Where(c => c.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(c => c.Name, model.Name)
                            .SetProperty(c => c.IndexerId, model.IndexerId)
                            .SetProperty(c => c.Enabled, model.Enabled)
                            .SetProperty(c => c.Status, model.Status)
                            .SetProperty(c => c.LastChecked, model.LastChecked)
                            .SetProperty(c => c.Settings, model.Settings)
                            .SetProperty(c => c.ModifiedAt, now)
                            .SetProperty(c => c.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(IndexerConnection),
                        Detail = $"Updated indexer connection {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating indexer connection {IndexerConnectionId}", model.Id);
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
                var now = DateTime.UtcNow;
                var model = await context.IndexerConnections
                    .AsNoTracking()
                    .Where(c => c.Id == id)
                    .Select(c => new { c.Name })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var deleted = await context.IndexerConnections
                    .Where(c => c.Id == id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(IndexerConnection),
                        Detail = $"Deleted indexer connection {model.Name}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting indexer connection {IndexerConnectionId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> PatchAsync(
        IndexerConnectionPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var model = await context.IndexerConnections
                    .FirstOrDefaultAsync(c => c.Id == patch.Id, cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var now = DateTime.UtcNow;
                if (patch.Name is not null)
                {
                    model.Name = patch.Name.Value;
                }
                if (patch.Enabled is not null)
                {
                    model.Enabled = patch.Enabled.Value;
                }
                if (patch.Settings is not null)
                {
                    if (patch.Settings.Add is not null)
                    {
                        foreach (var (key, value) in patch.Settings.Add)
                        {
                            model.Settings[key] = value ?? string.Empty;
                        }
                    }
                    if (patch.Settings.Update is not null)
                    {
                        foreach (var (key, value) in patch.Settings.Update)
                        {
                            if (model.Settings.ContainsKey(key))
                            {
                                model.Settings[key] = value ?? string.Empty;
                            }
                        }
                    }
                    if (patch.Settings.Remove is not null)
                    {
                        foreach (var key in patch.Settings.Remove)
                        {
                            model.Settings.Remove(key);
                        }
                    }

                    context.Entry(model).Property(m => m.Settings).IsModified = true;
                }

                var tagsAdded = new List<string>();
                var tagsRemoved = new List<string>();

                if (patch.Tags is not null)
                {
                    if (patch.Tags.Add is not null)
                    {
                        foreach (var tagName in patch.Tags.Add)
                        {
                            var tag = await context.Tags
                                .Include(t => t.IndexerConnections)
                                .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                .ConfigureAwait(false);

                            if (tag is null)
                            {
                                tag = new() { Name = tagName };
                                context.Tags.Add(tag);
                            }
                            if (!tag.IndexerConnections.Contains(model))
                            {
                                tag.IndexerConnections.Add(model);
                                tagsAdded.Add(tagName);
                            }
                        }
                    }
                    if (patch.Tags.Remove is not null)
                    {
                        foreach (var tagName in patch.Tags.Remove)
                        {
                            var tag = await context.Tags
                                .Include(t => t.IndexerConnections)
                                .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                .ConfigureAwait(false);

                            if (tag is not null && tag.IndexerConnections.Remove(model))
                            {
                                tagsRemoved.Add(tagName);
                            }
                        }
                    }
                }

                model.ModifiedAt = now;
                model.ModifiedByUserId = modifiedByUserId;

                var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Patch",
                        EntityId = patch.Id.ToString(),
                        EntityType = nameof(IndexerConnection),
                        Detail = $"Patched indexer connection {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);

                    foreach (var tagName in tagsAdded)
                    {
                        await auditRecordRepository.CreateAsync(new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "AddTag",
                            EntityId = patch.Id.ToString(),
                            EntityType = nameof(IndexerConnection),
                            Detail = $"Added tag '{tagName}' to indexer connection {model.Name}",
                            Timestamp = now,
                            UserId = modifiedByUserId
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    foreach (var tagName in tagsRemoved)
                    {
                        await auditRecordRepository.CreateAsync(new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "RemoveTag",
                            EntityId = patch.Id.ToString(),
                            EntityType = nameof(IndexerConnection),
                            Detail = $"Removed tag '{tagName}' from indexer connection {model.Name}",
                            Timestamp = now,
                            UserId = modifiedByUserId
                        }, cancellationToken).ConfigureAwait(false);
                    }
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error patching indexer connection {IndexerConnectionId}", patch.Id);
            throw;
        }
    }
}
