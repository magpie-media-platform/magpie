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
/// Repository for managing content filters.
/// </summary>
public sealed class ContentFilterRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<ContentFilterRepository> logger) : IContentFilterRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<ContentFilter>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.ContentFilters
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing content filters");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ContentFilter?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.ContentFilters
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting content filter {ContentFilterId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ContentFilter?> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            return await context.ContentFilters
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Name == name, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting content filter by name {ContentFilterName}", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ContentFilter>> ListByTagAsync(
        string tagName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.Tags
                .AsNoTracking()
                .Where(t => t.Name == tagName)
                .SelectMany(t => t.ContentFilters)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing content filters by tag {TagName}", tagName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ContentFilter>> ListByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.ContentFilters
                .AsNoTracking()
                .Where(f => f.OwnerUserId == userId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing content filters for user {UserId}", userId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ContentFilter> CreateAsync(
        ContentFilter model,
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

                context.ContentFilters.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.System,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(ContentFilter),
                    Detail = $"Created content filter {model.Name}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating content filter {ContentFilterName}", model.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        ContentFilter model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;

                var updated = await context.ContentFilters
                    .Where(f => f.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(f => f.Name, model.Name)
                            .SetProperty(f => f.Criteria, model.Criteria)
                            .SetProperty(f => f.OwnerUserId, model.OwnerUserId)
                            .SetProperty(f => f.OwnerLock, model.OwnerLock)
                            .SetProperty(f => f.ModifiedAt, now)
                            .SetProperty(f => f.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(ContentFilter),
                        Detail = $"Updated content filter {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating content filter {ContentFilterId}", model.Id);
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

                var model = await context.ContentFilters
                    .AsNoTracking()
                    .Where(f => f.Id == id)
                    .Select(f => new { f.Name })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var deleted = await context.ContentFilters
                    .Where(f => f.Id == id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(ContentFilter),
                        Detail = $"Deleted content filter {model.Name}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting content filter {ContentFilterId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> PatchAsync(
        ContentFilterPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var model = await context.ContentFilters
                    .FirstOrDefaultAsync(f => f.Id == patch.Id, cancellationToken)
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
                if (patch.Criteria is not null)
                {
                    model.Criteria = patch.Criteria.Value;
                }
                if (patch.OwnerLock is not null)
                {
                    model.OwnerLock = patch.OwnerLock.Value;
                }

                var tagsAdded = new List<string>();
                var tagsRemoved = new List<string>();
                if (patch.Tags is not null)
                {
                    var currentTags = await context.Tags
                        .Where(t => t.ContentFilters.Any(f => f.Id == patch.Id))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var tagName in patch.Tags.Remove ?? [])
                    {
                        var tag = currentTags.FirstOrDefault(t => t.Name == tagName);
                        if (tag is not null)
                        {
                            tag.ContentFilters.Remove(model);
                            tagsRemoved.Add(tagName);
                        }
                    }

                    foreach (var tagName in patch.Tags.Add ?? [])
                    {
                        if (!currentTags.Any(t => t.Name == tagName))
                        {
                            var tag = await context.Tags
                                .Include(t => t.ContentFilters)
                                .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                .ConfigureAwait(false);
                            if (tag is null)
                            {
                                tag = new Tag { Name = tagName };
                                context.Tags.Add(tag);
                            }
                            tag.ContentFilters.Add(model);
                            tagsAdded.Add(tagName);
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
                        Category = AuditCategories.System,
                        Operation = "Patch",
                        EntityId = patch.Id.ToString(),
                        EntityType = nameof(ContentFilter),
                        Detail = $"Patched content filter {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);

                    foreach (var tagName in tagsAdded)
                    {
                        await auditRecordRepository.CreateAsync(new()
                        {
                            Category = AuditCategories.System,
                            Operation = "AddTag",
                            EntityId = patch.Id.ToString(),
                            EntityType = nameof(ContentFilter),
                            Detail = $"Added tag {tagName} to content filter {model.Name}",
                            Timestamp = now,
                            UserId = modifiedByUserId
                        }, cancellationToken).ConfigureAwait(false);
                    }

                    foreach (var tagName in tagsRemoved)
                    {
                        await auditRecordRepository.CreateAsync(new()
                        {
                            Category = AuditCategories.System,
                            Operation = "RemoveTag",
                            EntityId = patch.Id.ToString(),
                            EntityType = nameof(ContentFilter),
                            Detail = $"Removed tag {tagName} from content filter {model.Name}",
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
            logger.LogError(ex, "Error patching content filter {ContentFilterId}", patch.Id);
            throw;
        }
    }
}
