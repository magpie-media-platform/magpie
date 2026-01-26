using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.Content;
using Magpie.Models.Filters;
using Magpie.Models.Filters.Content;
using Magpie.Models.History;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ContentHierarchyLevelEntity = Magpie.Data.Database.Entities.ContentHierarchyLevel;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing content hierarchy levels.
/// </summary>
public sealed class ContentHierarchyLevelRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    IMaterializedMetadataRepository materializedMetadataRepository,
    ILogger<ContentHierarchyLevelRepository> logger) : IContentHierarchyLevelRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ContentHierarchyLevel>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.ContentHierarchyLevels.AsNoTracking()
                .Include(c => c.LanguagePreferences)
                .Include(c => c.MetadataContainer).ThenInclude(m => m!.Contributions)
                .Include(c => c.ContentItem).ThenInclude(ci => ci!.Tags)
                .Include(c => c.ContentItem).ThenInclude(ci => ci!.MediaFiles).ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<ContentHierarchyLevel>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting content hierarchy levels ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<ContentHierarchyLevel>>(
                "Error retrieving content hierarchy levels",
                ErrorCodes.DbContentHierarchyLevelError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ContentHierarchyLevel?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from c in context.ContentHierarchyLevels
                    .AsNoTracking()
                    .Include(c => c.LanguagePreferences)
                    .Include(c => c.MetadataContainer).ThenInclude(m => m!.Contributions)
                    .Include(c => c.ContentItem).ThenInclude(ci => ci!.Tags)
                    .Include(c => c.ContentItem).ThenInclude(ci => ci!.MediaFiles)
                where c.Id == id
                select c)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting content hierarchy level {ContentHierarchyLevelId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<ContentHierarchyLevel?>(
                "Error retrieving content hierarchy level",
                ErrorCodes.DbContentHierarchyLevelError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ContentHierarchyLevel?>> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from c in context.ContentHierarchyLevels
                    .AsNoTracking()
                    .Include(c => c.LanguagePreferences)
                    .Include(c => c.MetadataContainer).ThenInclude(m => m!.Contributions)
                    .Include(c => c.ContentItem).ThenInclude(ci => ci!.Tags)
                    .Include(c => c.ContentItem).ThenInclude(ci => ci!.MediaFiles)
                where c.Slug == slug
                select c)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting content hierarchy level by slug {Slug} ({ExErrorCode})",
                slug,
                errorCode ?? "");
            return Result.Fail<ContentHierarchyLevel?>(
                "Error retrieving content hierarchy level",
                ErrorCodes.DbContentHierarchyLevelError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ContentHierarchyLevel>>> GetByRootFolderIdAsync(
        Guid rootFolderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from c in context.ContentHierarchyLevels
                    .AsNoTracking()
                    .Include(c => c.LanguagePreferences)
                    .Include(c => c.MetadataContainer).ThenInclude(m => m!.Contributions)
                    .Include(c => c.ContentItem).ThenInclude(ci => ci!.Tags)
                    .Include(c => c.ContentItem).ThenInclude(ci => ci!.MediaFiles)
                where c.RootFolderId == rootFolderId
                select c)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<ContentHierarchyLevel>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting content hierarchy levels for root folder {RootFolderId} ({ExErrorCode})",
                rootFolderId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<ContentHierarchyLevel>>(
                "Error retrieving content hierarchy levels",
                ErrorCodes.DbContentHierarchyLevelError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ContentHierarchyLevel>>> GetByParentIdAsync(
        Guid? parentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from c in context.ContentHierarchyLevels
                    .AsNoTracking()
                    .Include(c => c.LanguagePreferences)
                    .Include(c => c.MetadataContainer).ThenInclude(m => m!.Contributions)
                    .Include(c => c.ContentItem).ThenInclude(ci => ci!.Tags)
                    .Include(c => c.ContentItem).ThenInclude(ci => ci!.MediaFiles)
                where c.ParentId == parentId
                select c)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<ContentHierarchyLevel>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting content hierarchy levels for parent {ParentId} ({ExErrorCode})",
                parentId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<ContentHierarchyLevel>>(
                "Error retrieving content hierarchy levels",
                ErrorCodes.DbContentHierarchyLevelError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ContentHierarchyLevel>>> GetByDefinitionIdAsync(
        Guid definitionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from c in context.ContentHierarchyLevels
                    .AsNoTracking()
                    .Include(c => c.LanguagePreferences)
                    .Include(c => c.MetadataContainer).ThenInclude(m => m!.Contributions)
                    .Include(c => c.ContentItem).ThenInclude(ci => ci!.Tags)
                    .Include(c => c.ContentItem).ThenInclude(ci => ci!.MediaFiles)
                where c.ContentHierarchyLevelDefinitionId == definitionId
                select c)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<ContentHierarchyLevel>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting content hierarchy levels for definition {DefinitionId} ({ExErrorCode})",
                definitionId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<ContentHierarchyLevel>>(
                "Error retrieving content hierarchy levels",
                ErrorCodes.DbContentHierarchyLevelError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ContentHierarchyLevel>> CreateAsync(
        ContentHierarchyLevel model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = model.CreateEntity(createdByUserId);

            foreach (var languagePreference in model.LanguagePreferences)
            {
                entity.LanguagePreferences.Add(languagePreference.CreateEntity(entity.Id));
            }

            context.ContentHierarchyLevels.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(ContentHierarchyLevel),
                description: $"Created content hierarchy level {model.Name}",
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
                "Error creating content hierarchy level {ContentHierarchyLevelName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<ContentHierarchyLevel>(
                "Error creating content hierarchy level",
                ErrorCodes.DbContentHierarchyLevelError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        ContentHierarchyLevel model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.ContentHierarchyLevels
                .Where(c => c.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Name, model.Name)
                        .SetProperty(e => e.Slug, model.Slug)
                        .SetProperty(e => e.RelativePath, model.RelativePath)
                        .SetProperty(e => e.DateLastRefreshed, model.DateLastRefreshed)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                var existingPreferences = await context.LanguagePreferences
        .Where(lp => lp.ContentHierarchyId == model.Id)
        .ToListAsync(cancellationToken)
        .ConfigureAwait(false);

                var existingIds = existingPreferences.Select(lp => lp.Id).ToHashSet();
                var modelIds = model.LanguagePreferences.Select(lp => lp.Id).ToHashSet();

                await context.LanguagePreferences
        .Where(lp => lp.ContentHierarchyId == model.Id)
        .Where(lp => !modelIds.Contains(lp.Id))
        .ExecuteDeleteAsync(cancellationToken)
        .ConfigureAwait(false);

                foreach (var pref in model.LanguagePreferences)
                {
                    if (existingIds.Contains(pref.Id))
                    {
                        await context.LanguagePreferences
                            .Where(lp => lp.Id == pref.Id)
                            .ExecuteUpdateAsync(
                                setter => setter
                                    .SetProperty(e => e.Ordinal, pref.Ordinal)
                                    .SetProperty(e => e.Language, pref.Language),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        context.LanguagePreferences.Add(pref.CreateEntity(model.Id));
                    }
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Update",
                    entityId: model.Id,
                    entityType: nameof(ContentHierarchyLevel),
                    description: $"Updated content hierarchy level {model.Name}",
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
                "Error updating content hierarchy level {ContentHierarchyLevelId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating content hierarchy level",
                ErrorCodes.DbContentHierarchyLevelError,
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

            var entity = await context.ContentHierarchyLevels
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            await context.LanguagePreferences
      .Where(lp => lp.ContentHierarchyId == id)
      .ExecuteDeleteAsync(cancellationToken)
      .ConfigureAwait(false);

            var deleted = await context.ContentHierarchyLevels
                .Where(c => c.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(ContentHierarchyLevel),
                    description: $"Deleted content hierarchy level {entity.Name}",
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
                "Error deleting content hierarchy level {ContentHierarchyLevelId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error deleting content hierarchy level",
                ErrorCodes.DbContentHierarchyLevelError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        ContentHierarchyLevelPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.ContentHierarchyLevels
                .Include(c => c.LanguagePreferences)
                .FirstOrDefaultAsync(c => c.Id == patch.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            if (patch.RelativePath is not null)
            {
                entity.RelativePath = patch.RelativePath.Value;
            }

            if (patch.LanguagePreferences is not null)
            {
                if (patch.LanguagePreferences.Remove is not null)
                {
                    await context.LanguagePreferences
                        .Where(lp => lp.ContentHierarchyId == patch.Id)
                        .Where(lp => patch.LanguagePreferences.Remove.Contains(lp.Id))
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                if (patch.LanguagePreferences.Update is not null)
                {
                    foreach (var prefPatch in patch.LanguagePreferences.Update)
                    {
                        var existingPref = entity.LanguagePreferences.FirstOrDefault(lp => lp.Id == prefPatch.Id);
                        if (existingPref is not null)
                        {
                            if (prefPatch.Ordinal is not null)
                            {
                                existingPref.Ordinal = prefPatch.Ordinal.Value;
                            }
                            if (prefPatch.Language is not null)
                            {
                                existingPref.Language = prefPatch.Language.Value!;
                            }
                        }
                    }
                }

                if (patch.LanguagePreferences.Add is not null)
                {
                    foreach (var newPref in patch.LanguagePreferences.Add)
                    {
                        entity.LanguagePreferences.Add(newPref.CreateEntity(patch.Id));
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
                    entityType: nameof(ContentHierarchyLevel),
                    description: $"Patched content hierarchy level {entity.Name}",
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
                "Error patching content hierarchy level {ContentHierarchyLevelId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error patching content hierarchy level",
                ErrorCodes.DbContentHierarchyLevelError,
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
            var updated = await context.ContentHierarchyLevels
                .Where(c => c.Id == id)
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
                "Error updating content hierarchy level last refreshed time {ContentHierarchyLevelId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating content hierarchy level last refreshed time",
                ErrorCodes.DbContentHierarchyLevelError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<ContentHierarchyLevel>>> QueryAsync(
        ContentItemCriteria criteria,
        PagingRequest? paging,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = context.ContentHierarchyLevels
                .AsNoTracking()
                .Include(c => c.LanguagePreferences)
                .Include(c => c.MetadataContainer).ThenInclude(m => m!.Contributions)
                .Include(c => c.ContentItem).ThenInclude(ci => ci!.Tags)
                .Include(c => c.ContentItem).ThenInclude(ci => ci!.MediaFiles)
                .AsQueryable();

            if (criteria.TargetLevel.Metadata.Any.Count > 0)
            {
                foreach (var metadataCriteria in criteria.TargetLevel.Metadata.Any)
                {
                    var matchingContainerIds = materializedMetadataRepository.GetMatchingContainerIds(metadataCriteria);
                    query = query.Where(c => c.MetadataContainer != null &&
                        matchingContainerIds.Contains(c.MetadataContainer.Id));
                }
            }

            query = ApplyContentHierarchyLevelCriteria(query, criteria.TargetLevel);

            query = ApplyContentItemCriteria(query, criteria);

            query = query.OrderBy(c => c.Id);

            var totalItems = await query.CountAsync(cancellationToken).ConfigureAwait(false);

            if (paging is not null)
            {
                query = query.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);
            }

            var entities = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
            var results = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success(new PagedResult<ContentHierarchyLevel>(
                results,
                paging?.PageNumber,
                paging?.PageSize,
                totalItems));
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error querying content hierarchy levels ({ExErrorCode})",
                errorCode ?? "");
            return Result.Fail<PagedResult<ContentHierarchyLevel>>(
                "Error querying content hierarchy levels",
                ErrorCodes.DbContentHierarchyLevelError,
                errorCode);
        }
    }

    private static IQueryable<ContentHierarchyLevelEntity> ApplyContentHierarchyLevelCriteria(
        IQueryable<ContentHierarchyLevelEntity> query,
        ContentHierarchyLevelCriteria criteria)
    {
        query = ApplyGuidCriteria(
            query,
            criteria.ContentHierarchyLevelDefinitionId,
            c => c.ContentHierarchyLevelDefinitionId);
        query = ApplyStringCriteria(query, criteria.Name, c => c.Name);
        query = ApplyStringCriteria(query, criteria.Slug, c => c.Slug);
        query = ApplyNullableStringCriteria(query, criteria.RelativePath, c => c.RelativePath);
        query = ApplyDateTimeOffsetCriteria(query, criteria.CreatedAt, c => c.CreatedAt);
        query = ApplyNullableDateTimeOffsetCriteria(query, criteria.ModifiedAt, c => c.ModifiedAt);
        query = ApplyNullableDateTimeOffsetCriteria(query, criteria.DateLastRefreshed, c => c.DateLastRefreshed);
        query = ApplyTagsCriteria(query, criteria.Tags);

        return query;
    }

    private static IQueryable<ContentHierarchyLevelEntity> ApplyContentItemCriteria(
        IQueryable<ContentHierarchyLevelEntity> query,
        ContentItemCriteria criteria)
    {
        if (criteria.Monitoring.Any.Count > 0)
        {
            var monitoringValues = criteria.Monitoring.Any
                .Where(c => c.Operator == FieldOperator.Equals && c.Value != default)
                .Select(c => c.Value)
                .ToList();

            if (monitoringValues.Count > 0)
            {
                query = query.Where(c => c.ContentItem != null && monitoringValues.Contains(c.ContentItem.Monitoring));
            }
        }

        if (criteria.Fulfillment.Any.Count > 0)
        {
            var fulfillmentValues = criteria.Fulfillment.Any
                .Where(c => c.Operator == FieldOperator.Equals && c.Value != default)
                .Select(c => c.Value)
                .ToList();

            if (fulfillmentValues.Count > 0)
            {
                query = query.Where(c =>
                    c.ContentItem != null && fulfillmentValues.Contains(c.ContentItem.Fulfillment));
            }
        }

        if (criteria.QualityTierListId.Any.Count > 0)
        {
            var qualityTierListIds = criteria.QualityTierListId.Any
                .Where(c => c.Operator == FieldOperator.Equals && c.Value != default)
                .Select(c => c.Value)
                .ToList();

            if (qualityTierListIds.Count > 0)
            {
                query = query.Where(c => c.ContentItem != null &&
                    c.ContentItem.QualityTierListId != null &&
                    qualityTierListIds.Contains(c.ContentItem.QualityTierListId.Value));
            }
        }

        query = ApplyContentItemDateTimeOffsetCriteria(query, criteria.CreatedAt, ci => ci.CreatedAt);

        query = ApplyContentItemNullableDateTimeOffsetCriteria(query, criteria.ModifiedAt, ci => ci.ModifiedAt);

        query = ApplyContentItemNullableDateTimeOffsetCriteria(
            query,
            criteria.DateLastSearched, ci =>
            ci.DateLastSearched);

        query = ApplyContentItemTagsCriteria(query, criteria.Tags);

        return query;
    }

    private static IQueryable<ContentHierarchyLevelEntity> ApplyGuidCriteria(
        IQueryable<ContentHierarchyLevelEntity> query,
        AnyOfCriteria<FieldCriterion<Guid>> criteria,
        Expression<Func<ContentHierarchyLevelEntity, Guid>> selector)
    {
        if (criteria.Any.Count == 0)
        {
            return query;
        }

        var values = criteria.Any
            .Where(c => c.Operator == FieldOperator.Equals && c.Value != default)
            .Select(c => c.Value)
            .ToList();

        if (values.Count == 0)
        {
            return query;
        }

        var parameter = Expression.Parameter(typeof(ContentHierarchyLevelEntity), "c");
        var selectorBody = new ParameterReplacer(selector.Parameters[0], parameter).Visit(selector.Body);
        var containsMethod = typeof(List<Guid>).GetMethod("Contains", [typeof(Guid)])!;
        var containsCall = Expression.Call(Expression.Constant(values), containsMethod, selectorBody!);
        var predicate = Expression.Lambda<Func<ContentHierarchyLevelEntity, bool>>(containsCall, parameter);

        return query.Where(predicate);
    }

    private static IQueryable<ContentHierarchyLevelEntity> ApplyStringCriteria(
        IQueryable<ContentHierarchyLevelEntity> query,
        AnyOfCriteria<FieldCriterion<string>> criteria,
        Expression<Func<ContentHierarchyLevelEntity, string>> selector)
    {
        if (criteria.Any.Count == 0)
        {
            return query;
        }

        var predicates = new List<Expression<Func<ContentHierarchyLevelEntity, bool>>>();

        foreach (var criterion in criteria.Any)
        {
            var value = criterion.Value;
            switch (criterion.Operator)
            {
                case FieldOperator.Equals:
                    predicates.Add(CombineWithSelector(selector, v => v == value));
                    break;
                case FieldOperator.NotEquals:
                    predicates.Add(CombineWithSelector(selector, v => v != value));
                    break;
                case FieldOperator.ContainsString:
                    if (value is not null)
                    {
                        predicates.Add(CombineWithSelector(selector, v => v.Contains(value)));
                    }
                    break;
                case FieldOperator.StartsWithString:
                    if (value is not null)
                    {
                        predicates.Add(CombineWithSelector(selector, v => v.StartsWith(value)));
                    }
                    break;
                case FieldOperator.EndsWithString:
                    if (value is not null)
                    {
                        predicates.Add(CombineWithSelector(selector, v => v.EndsWith(value)));
                    }
                    break;
                case FieldOperator.In:
                    if (criterion.Values is { Count: > 0 })
                    {
                        var values = criterion.Values;
                        predicates.Add(CombineWithSelector(selector, v => values.Contains(v)));
                    }
                    break;
            }
        }

        if (predicates.Count == 0)
        {
            return query;
        }

        return query.Where(OrPredicates(predicates));
    }

    private static IQueryable<ContentHierarchyLevelEntity> ApplyNullableStringCriteria(
        IQueryable<ContentHierarchyLevelEntity> query,
        AnyOfCriteria<FieldCriterion<string>> criteria,
        Expression<Func<ContentHierarchyLevelEntity, string?>> selector)
    {
        if (criteria.Any.Count == 0)
        {
            return query;
        }

        var predicates = new List<Expression<Func<ContentHierarchyLevelEntity, bool>>>();

        foreach (var criterion in criteria.Any)
        {
            var value = criterion.Value;
            switch (criterion.Operator)
            {
                case FieldOperator.Equals:
                    predicates.Add(CombineWithNullableSelector(selector, v => v == value));
                    break;
                case FieldOperator.NotEquals:
                    predicates.Add(CombineWithNullableSelector(selector, v => v != value));
                    break;
                case FieldOperator.IsNull:
                    predicates.Add(CombineWithNullableSelector(selector, v => v == null));
                    break;
                case FieldOperator.IsNotNull:
                    predicates.Add(CombineWithNullableSelector(selector, v => v != null));
                    break;
                case FieldOperator.ContainsString:
                    if (value is not null)
                    {
                        predicates.Add(CombineWithNullableSelector(selector, v => v != null && v.Contains(value)));
                    }
                    break;
                case FieldOperator.StartsWithString:
                    if (value is not null)
                    {
                        predicates.Add(CombineWithNullableSelector(selector, v => v != null && v.StartsWith(value)));
                    }
                    break;
                case FieldOperator.EndsWithString:
                    if (value is not null)
                    {
                        predicates.Add(CombineWithNullableSelector(selector, v => v != null && v.EndsWith(value)));
                    }
                    break;
                case FieldOperator.In:
                    if (criterion.Values is { Count: > 0 })
                    {
                        var values = criterion.Values;
                        predicates.Add(CombineWithNullableSelector(selector, v => v != null && values.Contains(v)));
                    }
                    break;
            }
        }

        if (predicates.Count == 0)
        {
            return query;
        }

        return query.Where(OrPredicates(predicates));
    }

    private static IQueryable<ContentHierarchyLevelEntity> ApplyDateTimeOffsetCriteria(
        IQueryable<ContentHierarchyLevelEntity> query,
        AnyOfCriteria<FieldCriterion<DateTimeOffset>> criteria,
        Expression<Func<ContentHierarchyLevelEntity, DateTimeOffset>> selector)
    {
        if (criteria.Any.Count == 0)
        {
            return query;
        }

        var predicates = new List<Expression<Func<ContentHierarchyLevelEntity, bool>>>();

        foreach (var criterion in criteria.Any)
        {
            var value = criterion.Value;
            switch (criterion.Operator)
            {
                case FieldOperator.Equals:
                    predicates.Add(CombineWithSelector(selector, v => v == value));
                    break;
                case FieldOperator.NotEquals:
                    predicates.Add(CombineWithSelector(selector, v => v != value));
                    break;
                case FieldOperator.GreaterThan:
                    predicates.Add(CombineWithSelector(selector, v => v > value));
                    break;
                case FieldOperator.GreaterThanOrEqual:
                    predicates.Add(CombineWithSelector(selector, v => v >= value));
                    break;
                case FieldOperator.LessThan:
                    predicates.Add(CombineWithSelector(selector, v => v < value));
                    break;
                case FieldOperator.LessThanOrEqual:
                    predicates.Add(CombineWithSelector(selector, v => v <= value));
                    break;
                case FieldOperator.Between:
                    if (criterion.Range is not null)
                    {
                        var min = criterion.Range.Min;
                        var max = criterion.Range.Max;
                        predicates.Add(CombineWithSelector(selector, v => v >= min && v <= max));
                    }
                    break;
            }
        }

        if (predicates.Count == 0)
        {
            return query;
        }

        return query.Where(OrPredicates(predicates));
    }

    private static IQueryable<ContentHierarchyLevelEntity> ApplyNullableDateTimeOffsetCriteria(
        IQueryable<ContentHierarchyLevelEntity> query,
        AnyOfCriteria<FieldCriterion<DateTimeOffset>> criteria,
        Expression<Func<ContentHierarchyLevelEntity, DateTimeOffset?>> selector)
    {
        if (criteria.Any.Count == 0)
        {
            return query;
        }

        var predicates = new List<Expression<Func<ContentHierarchyLevelEntity, bool>>>();

        foreach (var criterion in criteria.Any)
        {
            var value = criterion.Value;
            switch (criterion.Operator)
            {
                case FieldOperator.Equals:
                    predicates.Add(CombineWithNullableSelector(selector, v => v == value));
                    break;
                case FieldOperator.NotEquals:
                    predicates.Add(CombineWithNullableSelector(selector, v => v != value));
                    break;
                case FieldOperator.IsNull:
                    predicates.Add(CombineWithNullableSelector(selector, v => v == null));
                    break;
                case FieldOperator.IsNotNull:
                    predicates.Add(CombineWithNullableSelector(selector, v => v != null));
                    break;
                case FieldOperator.GreaterThan:
                    predicates.Add(CombineWithNullableSelector(selector, v => v > value));
                    break;
                case FieldOperator.GreaterThanOrEqual:
                    predicates.Add(CombineWithNullableSelector(selector, v => v >= value));
                    break;
                case FieldOperator.LessThan:
                    predicates.Add(CombineWithNullableSelector(selector, v => v < value));
                    break;
                case FieldOperator.LessThanOrEqual:
                    predicates.Add(CombineWithNullableSelector(selector, v => v <= value));
                    break;
                case FieldOperator.Between:
                    if (criterion.Range is not null)
                    {
                        var min = criterion.Range.Min;
                        var max = criterion.Range.Max;
                        predicates.Add(CombineWithNullableSelector(selector, v => v >= min && v <= max));
                    }
                    break;
            }
        }

        if (predicates.Count == 0)
        {
            return query;
        }

        return query.Where(OrPredicates(predicates));
    }

    private static IQueryable<ContentHierarchyLevelEntity> ApplyTagsCriteria(
        IQueryable<ContentHierarchyLevelEntity> query,
        CollectionCriteria<FieldCriterion<string>> criteria)
    {
        if (criteria.Criteria.Count == 0)
        {
            return query;
        }

        var tagValues = criteria.Criteria
            .Where(c => c.Operator == FieldOperator.Equals && c.Value is not null)
            .Select(c => c.Value!)
            .ToList();

        if (tagValues.Count == 0)
        {
            return query;
        }

        return criteria.Operator switch
        {
            CollectionOperator.Any => query.Where(c =>
                c.ContentItem != null && c.ContentItem.Tags.Any(t => tagValues.Contains(t.TagName))),
            CollectionOperator.All => query.Where(c =>
                c.ContentItem != null && tagValues.All(tv => c.ContentItem.Tags.Any(t => t.TagName == tv))),
            CollectionOperator.None => query.Where(c =>
                c.ContentItem == null || !c.ContentItem.Tags.Any(t => tagValues.Contains(t.TagName))),
            _ => query
        };
    }

    private static IQueryable<ContentHierarchyLevelEntity> ApplyContentItemTagsCriteria(
        IQueryable<ContentHierarchyLevelEntity> query,
        CollectionCriteria<FieldCriterion<string>> criteria)
    {
        if (criteria.Criteria.Count == 0)
        {
            return query;
        }

        var tagValues = criteria.Criteria
            .Where(c => c.Operator == FieldOperator.Equals && c.Value is not null)
            .Select(c => c.Value!)
            .ToList();

        if (tagValues.Count == 0)
        {
            return query;
        }

        return criteria.Operator switch
        {
            CollectionOperator.Any => query.Where(c =>
                c.ContentItem != null && c.ContentItem.Tags.Any(t => tagValues.Contains(t.TagName))),
            CollectionOperator.All => query.Where(c =>
                c.ContentItem != null && tagValues.All(tv => c.ContentItem.Tags.Any(t => t.TagName == tv))),
            CollectionOperator.None => query.Where(c =>
                c.ContentItem == null || !c.ContentItem.Tags.Any(t => tagValues.Contains(t.TagName))),
            _ => query
        };
    }

    private static IQueryable<ContentHierarchyLevelEntity> ApplyContentItemDateTimeOffsetCriteria(
        IQueryable<ContentHierarchyLevelEntity> query,
        AnyOfCriteria<FieldCriterion<DateTimeOffset>> criteria,
        Expression<Func<Magpie.Data.Database.Entities.ContentItem, DateTimeOffset>> contentItemSelector)
    {
        if (criteria.Any.Count == 0)
        {
            return query;
        }

        var predicates = new List<Expression<Func<ContentHierarchyLevelEntity, bool>>>();

        foreach (var criterion in criteria.Any)
        {
            var value = criterion.Value;
            switch (criterion.Operator)
            {
                case FieldOperator.Equals:
                    predicates.Add(c => c.ContentItem != null && contentItemSelector.Compile()(c.ContentItem) == value);
                    break;
                case FieldOperator.GreaterThan:
                    predicates.Add(c => c.ContentItem != null && contentItemSelector.Compile()(c.ContentItem) > value);
                    break;
                case FieldOperator.GreaterThanOrEqual:
                    predicates.Add(c => c.ContentItem != null && contentItemSelector.Compile()(c.ContentItem) >= value);
                    break;
                case FieldOperator.LessThan:
                    predicates.Add(c => c.ContentItem != null && contentItemSelector.Compile()(c.ContentItem) < value);
                    break;
                case FieldOperator.LessThanOrEqual:
                    predicates.Add(c => c.ContentItem != null && contentItemSelector.Compile()(c.ContentItem) <= value);
                    break;
                case FieldOperator.Between:
                    if (criterion.Range is not null)
                    {
                        var min = criterion.Range.Min;
                        var max = criterion.Range.Max;
                        predicates.Add(c => c.ContentItem != null &&
                            contentItemSelector.Compile()(c.ContentItem) >= min &&
                            contentItemSelector.Compile()(c.ContentItem) <= max);
                    }
                    break;
            }
        }

        if (predicates.Count == 0)
        {
            return query;
        }

        return query.Where(OrPredicates(predicates));
    }

    private static IQueryable<ContentHierarchyLevelEntity> ApplyContentItemNullableDateTimeOffsetCriteria(
        IQueryable<ContentHierarchyLevelEntity> query,
        AnyOfCriteria<FieldCriterion<DateTimeOffset>> criteria,
        Expression<Func<Magpie.Data.Database.Entities.ContentItem, DateTimeOffset?>> contentItemSelector)
    {
        if (criteria.Any.Count == 0)
        {
            return query;
        }

        var predicates = new List<Expression<Func<ContentHierarchyLevelEntity, bool>>>();

        foreach (var criterion in criteria.Any)
        {
            var value = criterion.Value;
            switch (criterion.Operator)
            {
                case FieldOperator.Equals:
                    predicates.Add(c => c.ContentItem != null && contentItemSelector.Compile()(c.ContentItem) == value);
                    break;
                case FieldOperator.IsNull:
                    predicates.Add(c => c.ContentItem == null || contentItemSelector.Compile()(c.ContentItem) == null);
                    break;
                case FieldOperator.IsNotNull:
                    predicates.Add(c => c.ContentItem != null && contentItemSelector.Compile()(c.ContentItem) != null);
                    break;
                case FieldOperator.GreaterThan:
                    predicates.Add(c => c.ContentItem != null && contentItemSelector.Compile()(c.ContentItem) > value);
                    break;
                case FieldOperator.GreaterThanOrEqual:
                    predicates.Add(c => c.ContentItem != null && contentItemSelector.Compile()(c.ContentItem) >= value);
                    break;
                case FieldOperator.LessThan:
                    predicates.Add(c => c.ContentItem != null && contentItemSelector.Compile()(c.ContentItem) < value);
                    break;
                case FieldOperator.LessThanOrEqual:
                    predicates.Add(c => c.ContentItem != null && contentItemSelector.Compile()(c.ContentItem) <= value);
                    break;
                case FieldOperator.Between:
                    if (criterion.Range is not null)
                    {
                        var min = criterion.Range.Min;
                        var max = criterion.Range.Max;
                        predicates.Add(c => c.ContentItem != null &&
                            contentItemSelector.Compile()(c.ContentItem) >= min &&
                            contentItemSelector.Compile()(c.ContentItem) <= max);
                    }
                    break;
            }
        }

        if (predicates.Count == 0)
        {
            return query;
        }

        return query.Where(OrPredicates(predicates));
    }

    private static Expression<Func<ContentHierarchyLevelEntity, bool>> CombineWithSelector<T>(
        Expression<Func<ContentHierarchyLevelEntity, T>> selector,
        Expression<Func<T, bool>> predicate)
    {
        var parameter = selector.Parameters[0];
        var selectorBody = selector.Body;
        var predicateBody = new ParameterReplacer(predicate.Parameters[0], selectorBody).Visit(predicate.Body);
        return Expression.Lambda<Func<ContentHierarchyLevelEntity, bool>>(predicateBody!, parameter);
    }

    private static Expression<Func<ContentHierarchyLevelEntity, bool>> CombineWithNullableSelector<T>(
        Expression<Func<ContentHierarchyLevelEntity, T?>> selector,
        Expression<Func<T?, bool>> predicate)
    {
        var parameter = selector.Parameters[0];
        var selectorBody = selector.Body;
        var predicateBody = new ParameterReplacer(predicate.Parameters[0], selectorBody).Visit(predicate.Body);
        return Expression.Lambda<Func<ContentHierarchyLevelEntity, bool>>(predicateBody!, parameter);
    }

    private static Expression<Func<ContentHierarchyLevelEntity, bool>> OrPredicates(
        List<Expression<Func<ContentHierarchyLevelEntity, bool>>> predicates)
    {
        if (predicates.Count == 1)
        {
            return predicates[0];
        }

        var parameter = Expression.Parameter(typeof(ContentHierarchyLevelEntity), "c");
        Expression? combined = null;

        foreach (var predicate in predicates)
        {
            var body = new ParameterReplacer(predicate.Parameters[0], parameter).Visit(predicate.Body);
            combined = combined is null ? body : Expression.OrElse(combined, body!);
        }

        return Expression.Lambda<Func<ContentHierarchyLevelEntity, bool>>(combined!, parameter);
    }

    private sealed class ParameterReplacer(Expression from, Expression to) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node)
        {
            return node == from ? to : base.Visit(node);
        }
    }
}
