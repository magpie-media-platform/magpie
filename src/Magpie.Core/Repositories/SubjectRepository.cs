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
/// Repository for managing subjects.
/// </summary>
public sealed class SubjectRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<SubjectRepository> logger) : ISubjectRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<Subject>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.Subjects
                .AsNoTracking()
                .Include(s => s.MetadataContainer)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing subjects");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Subject?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Subjects
                .AsNoTracking()
                .Include(s => s.MetadataContainer)
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting subject {SubjectId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Subject>> GetByKindAsync(string kind, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Subjects
                .AsNoTracking()
                .Include(s => s.MetadataContainer)
                .Where(s => s.Kind == kind)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting subjects by kind {Kind}", kind);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Subject>> GetByTagAsync(string tagName, CancellationToken cancellationToken)
    {
        try
        {
            return await (
                from t in context.Tags
                from c in t.MetadataContainers
                join s in context.Subjects on c.Id equals s.MetadataContainerId
                where t.Name == tagName
                select s)
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting subjects by tag {TagName}", tagName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Subject> CreateAsync(
        Subject model,
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

                context.Subjects.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.System,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(Subject),
                    Detail = $"Created subject of kind {model.Kind}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating subject of kind {Kind}", model.Kind);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        Subject model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var updated = await context.Subjects
                    .Where(s => s.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(e => e.Kind, model.Kind)
                            .SetProperty(e => e.DateLastRefreshed, model.DateLastRefreshed)
                            .SetProperty(e => e.ModifiedAt, now)
                            .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(Subject),
                        Detail = $"Updated subject of kind {model.Kind}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating subject {SubjectId}", model.Id);
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
                var model = await context.Subjects
                    .AsNoTracking()
                    .Where(s => s.Id == id)
                    .Select(s => new { s.Kind })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var deleted = await context.Subjects
                    .Where(s => s.Id == id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(Subject),
                        Detail = $"Deleted subject of kind {model.Kind}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting subject {SubjectId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> PatchAsync(
        TagsPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var model = await context.Subjects
                    .Include(s => s.MetadataContainer)
                    .FirstOrDefaultAsync(s => s.Id == patch.Id, cancellationToken)
                    .ConfigureAwait(false);

                if (model is null || model.MetadataContainer is null)
                {
                    return false;
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
                                .Include(t => t.MetadataContainers)
                                .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                .ConfigureAwait(false);

                            if (tag is null)
                            {
                                tag = new() { Name = tagName };
                                context.Tags.Add(tag);
                            }
                            if (!tag.MetadataContainers.Contains(model.MetadataContainer))
                            {
                                tag.MetadataContainers.Add(model.MetadataContainer);
                                tagsAdded.Add(tagName);
                            }
                        }
                    }
                    if (patch.Tags.Remove is not null)
                    {
                        foreach (var tagName in patch.Tags.Remove)
                        {
                            var tag = await context.Tags
                                .Include(t => t.MetadataContainers)
                                .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                .ConfigureAwait(false);

                            if (tag is not null && tag.MetadataContainers.Remove(model.MetadataContainer))
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
                        Category = AuditCategories.System,
                        Operation = "Patch",
                        EntityId = patch.Id.ToString(),
                        EntityType = nameof(Subject),
                        Detail = $"Patched subject of kind {model.Kind}",
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
                            EntityType = nameof(Subject),
                            Detail = $"Added tag '{tagName}' to subject of kind {model.Kind}",
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
                            EntityType = nameof(Subject),
                            Detail = $"Removed tag '{tagName}' from subject of kind {model.Kind}",
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
            logger.LogError(ex, "Error patching subject {SubjectId}", patch.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateLastRefreshedAsync(
        Guid id,
        DateTime dateLastRefreshed,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.Subjects
                .Where(s => s.Id == id)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(e => e.DateLastRefreshed, dateLastRefreshed),
                    cancellationToken)
                .ConfigureAwait(false);

            return updated > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating subject last refreshed time {SubjectId}", id);
            throw;
        }
    }
}
