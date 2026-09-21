using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class IndexerConnectionRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<IndexerConnectionRepository> logger,
    IDataProtectionService protector,
    TimeProvider timeProvider) : IIndexerConnectionRepository
{
    private readonly TimeProvider _time = timeProvider;

    public async Task<IEnumerable<IndexerConnection>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connections = await context.IndexerConnections
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var connection in connections)
            {
                connection.Settings = protector.Unprotect(connection.Settings);
            }

            return connections;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer connection repository failed to list the indexer connections.");
            throw;
        }
    }

    public async Task<IndexerConnection?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await context.IndexerConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (connection is not null)
            {
                connection.Settings = protector.Unprotect(connection.Settings);
            }

            return connection;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer connection repository failed to read indexer connection " +
                "{IndexerConnectionId}.", id);
            throw;
        }
    }

    public async Task<PagedList<IndexerConnection>> QueryAsync(
        IndexerConnectionQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var connections = context.IndexerConnections.AsNoTracking().AsQueryable();

            if (query.IndexerId is not null)
            {
                connections = connections.Where(c => c.IndexerId == query.IndexerId);
            }

            if (query.Tag is not null)
            {
                var taggedIds =
                    from t in context.Tags.AsNoTracking()
                    where t.Name == query.Tag
                    from c in t.IndexerConnections
                    select c.Id;
                connections = connections.Where(c => taggedIds.Contains(c.Id));
            }

            var totalCount = await connections.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortableProperties = new[]
            {
                nameof(IndexerConnection.Id), nameof(IndexerConnection.IndexerId), nameof(IndexerConnection.Name),
                nameof(IndexerConnection.Enabled),
                nameof(IndexerConnection.Status),
                nameof(IndexerConnection.LastChecked),
                nameof(IndexerConnection.LastError),
                nameof(IndexerConnection.ConsecutiveFailures),
                nameof(IndexerConnection.CreatedAt),
                nameof(IndexerConnection.CreatedByUserId),
                nameof(IndexerConnection.ModifiedAt),
                nameof(IndexerConnection.ModifiedByUserId),
            };
            var sortProperty = sortableProperties
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                connections = sort.Direction == SortDirection.Ascending
                    ? connections
                        .OrderBy(c => EF.Property<object>(c, sortProperty))
                        .ThenBy(c => c.Id)
                    : connections
                        .OrderByDescending(c => EF.Property<object>(c, sortProperty))
                        .ThenBy(c => c.Id);
            }
            else
            {
                connections = connections.OrderBy(c => c.Name).ThenBy(c => c.Id);
            }

            connections = connections.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            var items = await connections.ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var connection in items)
            {
                connection.Settings = protector.Unprotect(connection.Settings);
            }

            return new()
            {
                Items = items,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalItems = totalCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer connection repository failed to query the indexer connections.");
            throw;
        }
    }

    public async Task<IndexerConnection> CreateAsync(
        IndexerConnection model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    model.CreatedAt = now;
                    model.CreatedByUserId = createdByUserId;

                    var plaintext = model.Settings;
                    model.Settings = protector.Protect(plaintext);

                    context.IndexerConnections.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    context.Entry(model).State = EntityState.Detached;
                    model.Settings = plaintext;

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(IndexerConnection),
                            Detail = $"Created indexer connection {model.Name}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer connection repository failed to create indexer connection " +
                "{IndexerConnectionName}.", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        IndexerConnection model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var encryptedSettings = protector.Protect(model.Settings);

                    var updated = await context.IndexerConnections
                        .Where(c => c.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(c => c.Name, model.Name)
                                .SetProperty(c => c.IndexerId, model.IndexerId)
                                .SetProperty(c => c.Enabled, model.Enabled)
                                .SetProperty(c => c.Status, model.Status)
                                .SetProperty(c => c.LastChecked, model.LastChecked)
                                .SetProperty(c => c.Settings, encryptedSettings)
                                .SetProperty(c => c.ModifiedAt, now)
                                .SetProperty(c => c.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Integrations,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(IndexerConnection),
                                Detail = $"Updated indexer connection {model.Name}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer connection repository failed to update indexer connection " +
                "{IndexerConnectionId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var model = await (
                        from c in context.IndexerConnections.AsNoTracking()
                        where c.Id == id
                        select new { c.Name })
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
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Integrations,
                                Operation = "Delete",
                                EntityId = id.ToString(),
                                EntityType = nameof(IndexerConnection),
                                Detail = $"Deleted indexer connection {model.Name}",
                                Timestamp = now,
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return deleted > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer connection repository failed to delete indexer connection " +
                "{IndexerConnectionId}.", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        IndexerConnectionPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var model = await context.IndexerConnections
                        .FirstOrDefaultAsync(c => c.Id == patch.Id, cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var now = _time.GetUtcNow().UtcDateTime;
                    if (patch.Name is not null)
                    {
                        model.Name = patch.Name.Value;
                    }

                    if (patch.Enabled is not null)
                    {
                        model.Enabled = patch.Enabled.Value;
                    }

                    if (patch.Status is not null)
                    {
                        model.Status = patch.Status.Value;
                    }

                    if (patch.LastChecked is not null)
                    {
                        model.LastChecked = patch.LastChecked.Value;
                    }

                    if (patch.LastError is not null)
                    {
                        var error = patch.LastError.Value;
                        model.LastError = error is { Length: > 1024 } ? error[..1024] : error;
                    }

                    if (patch.ConsecutiveFailures is not null)
                    {
                        model.ConsecutiveFailures = patch.ConsecutiveFailures.Value;
                    }

                    if (patch.Settings is not null)
                    {
                        var settings = protector.Unprotect(model.Settings);
                        if (patch.Settings.Add is not null)
                        {
                            foreach (var (key, value) in patch.Settings.Add)
                            {
                                settings[key] = value ?? string.Empty;
                            }
                        }

                        if (patch.Settings.Update is not null)
                        {
                            foreach (var (key, value) in patch.Settings.Update)
                            {
                                if (settings.ContainsKey(key))
                                {
                                    settings[key] = value ?? string.Empty;
                                }
                            }
                        }

                        if (patch.Settings.Remove is not null)
                        {
                            foreach (var key in patch.Settings.Remove)
                            {
                                settings.Remove(key);
                            }
                        }

                        model.Settings = protector.Protect(settings);
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
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Integrations,
                                Operation = "Patch",
                                EntityId = patch.Id.ToString(),
                                EntityType = nameof(IndexerConnection),
                                Detail = $"Updated indexer connection {model.Name}.",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);

                        foreach (var tagName in tagsAdded)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.Integrations,
                                    Operation = "AddTag",
                                    EntityId = patch.Id.ToString(),
                                    EntityType = nameof(IndexerConnection),
                                    Detail = $"Added tag {tagName} to indexer connection {model.Name}",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                },
                                cancellationToken).ConfigureAwait(false);
                        }

                        foreach (var tagName in tagsRemoved)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.Integrations,
                                    Operation = "RemoveTag",
                                    EntityId = patch.Id.ToString(),
                                    EntityType = nameof(IndexerConnection),
                                    Detail = $"Removed tag {tagName} from indexer connection {model.Name}",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                },
                                cancellationToken).ConfigureAwait(false);
                        }
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer connection repository failed to patch indexer connection " +
                "{IndexerConnectionId}.", patch.Id);
            throw;
        }
    }
}
