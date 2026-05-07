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
/// Repository for managing subject filters.
/// </summary>
public sealed class SubjectFilterRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<SubjectFilterRepository> logger) : ISubjectFilterRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<SubjectFilter>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SubjectFilters
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing subject filters");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SubjectFilter?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.SubjectFilters
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting subject filter {SubjectFilterId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SubjectFilter?> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            return await context.SubjectFilters
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Name == name, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting subject filter by name {SubjectFilterName}", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<SubjectFilter>> ListByTagAsync(string tagName, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Tags
                .AsNoTracking()
                .Where(t => t.Name == tagName)
                .SelectMany(t => t.SubjectFilters)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing subject filters by tag {TagName}", tagName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<SubjectFilter>> ListByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            return await context.SubjectFilters
                .AsNoTracking()
                .Where(f => f.OwnerUserId == userId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing subject filters by user {UserId}", userId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SubjectFilter> CreateAsync(
        SubjectFilter model,
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

                context.SubjectFilters.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.System,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(SubjectFilter),
                    Detail = $"Created subject filter {model.Name}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating subject filter {SubjectFilterName}", model.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        SubjectFilter model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var updated = await context.SubjectFilters
                    .Where(f => f.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(f => f.Name, model.Name)
                            .SetProperty(f => f.Criteria, model.Criteria)
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
                        EntityType = nameof(SubjectFilter),
                        Detail = $"Updated subject filter {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating subject filter {SubjectFilterId}", model.Id);
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
                var model = await context.SubjectFilters
                    .AsNoTracking()
                    .Where(f => f.Id == id)
                    .Select(f => new { f.Name })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var deleted = await context.SubjectFilters
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
                        EntityType = nameof(SubjectFilter),
                        Detail = $"Deleted subject filter {model.Name}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting subject filter {SubjectFilterId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> PatchAsync(
        SubjectFilterPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var model = await context.SubjectFilters
                    .FirstOrDefaultAsync(f => f.Id == patch.Id, cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                if (patch.Name is not null)
                {
                    model.Name = patch.Name.Value;
                }
                if (patch.Criteria is not null)
                {
                    model.Criteria = patch.Criteria.Value;
                }

                var tagsAdded = new List<string>();
                var tagsRemoved = new List<string>();
                if (patch.Tags is not null)
                {
                    var currentTags = await context.Tags
                        .Where(t => t.SubjectFilters.Any(f => f.Id == patch.Id))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var tagName in patch.Tags.Remove ?? [])
                    {
                        var tag = currentTags.FirstOrDefault(t => t.Name == tagName);
                        if (tag is not null)
                        {
                            tag.SubjectFilters.Remove(model);
                            tagsRemoved.Add(tagName);
                        }
                    }

                    foreach (var tagName in patch.Tags.Add ?? [])
                    {
                        if (!currentTags.Any(t => t.Name == tagName))
                        {
                            var tag = await context.Tags
                                .Include(t => t.SubjectFilters)
                                .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                .ConfigureAwait(false);
                            if (tag is null)
                            {
                                tag = new Tag { Name = tagName };
                                context.Tags.Add(tag);
                            }
                            tag.SubjectFilters.Add(model);
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
                        EntityType = nameof(SubjectFilter),
                        Detail = $"Patched subject filter {model.Name}",
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
                            EntityType = nameof(SubjectFilter),
                            Detail = $"Added tag {tagName} to subject filter {model.Name}",
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
                            EntityType = nameof(SubjectFilter),
                            Detail = $"Removed tag {tagName} from subject filter {model.Name}",
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
            logger.LogError(ex, "Error patching subject filter {SubjectFilterId}", patch.Id);
            throw;
        }
    }
}
