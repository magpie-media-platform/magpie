using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Models.Content;
using Magpie.Models.History;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using SubjectTagEntity = Magpie.Data.Database.Entities.SubjectTag;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing subjects.
/// </summary>
public sealed class SubjectRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<SubjectRepository> logger) : ISubjectRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Subject>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.Subjects
                .AsNoTracking()
                .Include(s => s.MetadataContainer)
                    .ThenInclude(mc => mc!.Contributions)
                .Include(s => s.Tags)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Subject>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting subjects ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<Subject>>(
                "Error retrieving subjects",
                ErrorCodes.DbSubjectError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Subject?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from s in context.Subjects
                    .AsNoTracking()
                    .Include(s => s.MetadataContainer)
                        .ThenInclude(mc => mc!.Contributions)
                    .Include(s => s.Tags)
                where s.Id == id
                select s)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting subject {SubjectId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<Subject?>(
                "Error retrieving subject",
                ErrorCodes.DbSubjectError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Subject>>> GetByKindAsync(
        string kind,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from s in context.Subjects
                    .AsNoTracking()
                    .Include(s => s.MetadataContainer)
                        .ThenInclude(mc => mc!.Contributions)
                    .Include(s => s.Tags)
                where s.Kind == kind
                select s)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Subject>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting subjects by kind {Kind} ({ExErrorCode})",
                kind,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<Subject>>(
                "Error retrieving subjects",
                ErrorCodes.DbSubjectError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Subject>>> GetByTagAsync(
        string tagName,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from s in context.Subjects
                    .AsNoTracking()
                    .Include(s => s.MetadataContainer)
                        .ThenInclude(mc => mc!.Contributions)
                    .Include(s => s.Tags)
                where s.Tags.Any(t => t.TagName == tagName)
                select s)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Subject>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting subjects by tag {TagName} ({ExErrorCode})",
                tagName,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<Subject>>(
                "Error retrieving subjects",
                ErrorCodes.DbSubjectError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Subject>> CreateAsync(
        Subject model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = model.CreateEntity(createdByUserId);

            foreach (var tag in model.Tags)
            {
                entity.Tags.Add(new SubjectTagEntity { TagName = tag });
            }

            context.Subjects.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(Subject),
                description: $"Created subject of kind {model.Kind}",
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
                "Error creating subject of kind {Kind} ({ExErrorCode})",
                model.Kind,
                errorCode ?? "");
            return Result.Fail<Subject>(
                "Error creating subject",
                ErrorCodes.DbSubjectError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        Subject model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.Subjects
                .Where(s => s.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Kind, model.Kind)
                        .SetProperty(e => e.DateLastRefreshed, model.DateLastRefreshed)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                var existingTags = await context.SubjectTags
                    .Where(t => t.SubjectId == model.Id)
                    .Select(t => t.TagName)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                await context.SubjectTags
                    .Where(t => t.SubjectId == model.Id)
                    .Where(t => !model.Tags.Contains(t.TagName))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var tag in model.Tags)
                {
                    if (!existingTags.Contains(tag))
                    {
                        context.SubjectTags.Add(new SubjectTagEntity
                        {
                            SubjectId = model.Id,
                            TagName = tag
                        });
                    }
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Update",
                    entityId: model.Id,
                    entityType: nameof(Subject),
                    description: $"Updated subject of kind {model.Kind}",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error updating subject {SubjectId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating subject",
                ErrorCodes.DbSubjectError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> DeleteAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.Subjects
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            await context.SubjectTags
                .Where(t => t.SubjectId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.Subjects
                .Where(s => s.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(Subject),
                    description: $"Deleted subject of kind {entity.Kind}",
                    userId: deletedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(deleted > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error deleting subject {SubjectId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error deleting subject",
                ErrorCodes.DbSubjectError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        SubjectPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.Subjects
                .Include(s => s.Tags)
                .FirstOrDefaultAsync(s => s.Id == patch.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            if (patch.Tags is not null)
            {
                if (patch.Tags.Remove is not null)
                {
                    await context.SubjectTags
                        .Where(t => t.SubjectId == patch.Id)
                        .Where(t => patch.Tags.Remove.Contains(t.TagName))
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                if (patch.Tags.Add is not null)
                {
                    var existingTags = await context.SubjectTags
                        .Where(t => t.SubjectId == patch.Id)
                        .Select(t => t.TagName)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var tag in patch.Tags.Add)
                    {
                        if (!existingTags.Contains(tag))
                        {
                            context.SubjectTags.Add(new SubjectTagEntity
                            {
                                SubjectId = patch.Id,
                                TagName = tag
                            });
                        }
                    }
                }
            }

            entity.ModifiedAt = DateTimeOffset.UtcNow;
            entity.ModifiedByUserId = modifiedByUserId;

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (updated > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Patch",
                    entityId: patch.Id,
                    entityType: nameof(Subject),
                    description: $"Patched subject of kind {entity.Kind}",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error patching subject {SubjectId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error patching subject",
                ErrorCodes.DbSubjectError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateLastRefreshedAsync(
        Guid id,
        DateTimeOffset dateLastRefreshed,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.Subjects
                .Where(s => s.Id == id)
                .ExecuteUpdateAsync(
                    setter => setter.SetProperty(e => e.DateLastRefreshed, dateLastRefreshed),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error updating subject last refreshed time {SubjectId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating subject last refreshed time",
                ErrorCodes.DbSubjectError,
                errorCode);
        }
    }
}
