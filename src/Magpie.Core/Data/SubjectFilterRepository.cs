using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Models.Filters;
using Magpie.Models.History;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using SubjectFilterTagEntity = Magpie.Data.Database.Entities.SubjectFilterTag;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing subject filters.
/// </summary>
public sealed class SubjectFilterRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<SubjectFilterRepository> logger) : ISubjectFilterRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SubjectFilter>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.SubjectFilters
                .AsNoTracking()
                .Include(f => f.Tags)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<SubjectFilter>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting subject filters ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<SubjectFilter>>(
                "Error retrieving subject filters",
                ErrorCodes.DbSubjectFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<SubjectFilter?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from f in context.SubjectFilters.AsNoTracking().Include(f => f.Tags)
                where f.Id == id
                select f)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting subject filter {SubjectFilterId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<SubjectFilter?>(
                "Error retrieving subject filter",
                ErrorCodes.DbSubjectFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<SubjectFilter?>> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from f in context.SubjectFilters.AsNoTracking().Include(f => f.Tags)
                where f.Name == name
                select f)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting subject filter {SubjectFilterName} ({ExErrorCode})",
                name,
                errorCode ?? "");
            return Result.Fail<SubjectFilter?>(
                "Error retrieving subject filter",
                ErrorCodes.DbSubjectFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SubjectFilter>>> GetByTagAsync(
        string tagName,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from f in context.SubjectFilters.AsNoTracking().Include(f => f.Tags)
                where f.Tags.Any(t => t.TagName == tagName)
                select f)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<SubjectFilter>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting subject filters by tag {TagName} ({ExErrorCode})",
                tagName,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<SubjectFilter>>(
                "Error retrieving subject filters",
                ErrorCodes.DbSubjectFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<SubjectFilter>> CreateAsync(
        SubjectFilter model,
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
                entity.Tags.Add(new SubjectFilterTagEntity { TagName = tag });
            }

            context.SubjectFilters.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(SubjectFilter),
                description: $"Created subject filter {model.Name}",
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
                "Error creating subject filter {SubjectFilterName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<SubjectFilter>(
                "Error creating subject filter",
                ErrorCodes.DbSubjectFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        SubjectFilter model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.SubjectFilters
                .Where(f => f.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Name, model.Name)
                        .SetProperty(e => e.Criteria, JsonSerializer.Serialize(model.Criteria))
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                var existingTags = await context.SubjectFilterTags
                    .Where(t => t.SubjectFilterId == model.Id)
                    .Select(t => t.TagName)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                await context.SubjectFilterTags
                    .Where(t => t.SubjectFilterId == model.Id)
                    .Where(t => !model.Tags.Contains(t.TagName))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var tag in model.Tags)
                {
                    if (!existingTags.Contains(tag))
                    {
                        context.SubjectFilterTags.Add(new SubjectFilterTagEntity
                        {
                            SubjectFilterId = model.Id,
                            TagName = tag
                        });
                    }
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Update",
                    entityId: model.Id,
                    entityType: nameof(SubjectFilter),
                    description: $"Updated subject filter {model.Name}",
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
                "Error updating subject filter {SubjectFilterId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating subject filter",
                ErrorCodes.DbSubjectFilterError,
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

            var entity = await context.SubjectFilters
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            await context.SubjectFilterTags
                .Where(t => t.SubjectFilterId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.SubjectFilters
                .Where(f => f.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(SubjectFilter),
                    description: $"Deleted subject filter {entity.Name}",
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
                "Error deleting subject filter {SubjectFilterId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error deleting subject filter",
                ErrorCodes.DbSubjectFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        SubjectFilterPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.SubjectFilters
                .Include(f => f.Tags)
                .FirstOrDefaultAsync(f => f.Id == patch.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            if (patch.Name is not null)
            {
                entity.Name = patch.Name.Value!;
            }

            if (patch.Criteria is not null)
            {
                entity.Criteria = JsonSerializer.Serialize(patch.Criteria.Value);
            }

            if (patch.Tags is not null)
            {
                if (patch.Tags.Remove is not null)
                {
                    await context.SubjectFilterTags
                        .Where(t => t.SubjectFilterId == patch.Id)
                        .Where(t => patch.Tags.Remove.Contains(t.TagName))
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                if (patch.Tags.Add is not null)
                {
                    var existingTags = await context.SubjectFilterTags
                        .Where(t => t.SubjectFilterId == patch.Id)
                        .Select(t => t.TagName)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var tag in patch.Tags.Add)
                    {
                        if (!existingTags.Contains(tag))
                        {
                            context.SubjectFilterTags.Add(new SubjectFilterTagEntity
                            {
                                SubjectFilterId = patch.Id,
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
                    entityType: nameof(SubjectFilter),
                    description: $"Patched subject filter {entity.Name}",
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
                "Error patching subject filter {SubjectFilterId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error patching subject filter",
                ErrorCodes.DbSubjectFilterError,
                errorCode);
        }
    }
}
