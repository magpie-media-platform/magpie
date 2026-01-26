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
using Magpie.Models.Filters;
using Magpie.Models.Filters.Metadata;
using Magpie.Models.Metadata;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MetadataContainerEntity = Magpie.Data.Database.Entities.MetadataContainer;
using MetadataContributionEntity = Magpie.Data.Database.Entities.MetadataContribution;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for materializing metadata and querying materialized views.
/// </summary>
public sealed class MaterializedMetadataRepository(
    MagpieDbContext context,
    ILogger<MaterializedMetadataRepository> logger) : IMaterializedMetadataRepository
{
    /// <inheritdoc />
    public async Task<Result<MaterializedMetadata?>> MaterializeAsync(
        Guid containerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var container = await context.MetadataContainers
                .AsNoTracking()
                .Include(mc => mc.Contributions).ThenInclude(c => c.CustomMetadataValues)
                .Include(mc => mc.Contributions).ThenInclude(c => c.ExternalIds)
                .Include(mc => mc.Contributions).ThenInclude(c => c.References)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Relationships)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Images)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Previews)
                .Include(mc => mc.Contributions).ThenInclude(c => c.AlternateTexts)
                .Include(mc => mc.Contributions).ThenInclude(c => c.ContentRatings)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Awards)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Genres)
                .FirstOrDefaultAsync(mc => mc.Id == containerId, cancellationToken)
                .ConfigureAwait(false);

            if (container is null)
            {
                return Result.Success<MaterializedMetadata?>(null);
            }

            var materialized = MaterializeFromContainer(container);
            return Result.Success<MaterializedMetadata?>(materialized);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error materializing metadata for container {ContainerId} ({ExErrorCode})",
                containerId,
                errorCode ?? "");
            return Result.Fail<MaterializedMetadata?>(
                "Error materializing metadata",
                ErrorCodes.DbMetadataError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public IQueryable<MaterializedProjection> GetMaterializedQuery()
    {
        return
            from mc in context.MetadataContainers.AsNoTracking()
            select new MaterializedProjection
            {
                ContainerId = mc.Id,
                Title = (
                    from c in mc.Contributions
                    orderby c.Ordinal
                    where c.Title != null
                    select c.Title).FirstOrDefault(),
                OriginalTitle = (
                    from c in mc.Contributions
                    orderby c.Ordinal
                    where c.OriginalTitle != null
                    select c.OriginalTitle).FirstOrDefault(),
                SortTitle = (
                    from c in mc.Contributions
                    orderby c.Ordinal
                    where c.SortTitle != null
                    select c.SortTitle).FirstOrDefault(),
                Language = (
                    from c in mc.Contributions
                    orderby c.Ordinal
                    where c.Language != null
                    select c.Language).FirstOrDefault(),
                Overview = (
                    from c in mc.Contributions
                    orderby c.Ordinal
                    where c.Overview != null
                    select c.Overview).FirstOrDefault(),
                PublishDate = (
                    from c in mc.Contributions
                    orderby c.Ordinal
                    where c.PublishDate != null
                    select c.PublishDate).FirstOrDefault(),
                Duration = (
                    from c in mc.Contributions
                    orderby c.Ordinal
                    where c.Duration != null
                    select c.Duration).FirstOrDefault(),
                ProductionStatus = (
                    from c in mc.Contributions
                    orderby c.Ordinal
                    where c.ProductionStatus != null
                    select c.ProductionStatus).FirstOrDefault(),
                Established = (
                    from c in mc.Contributions
                    orderby c.Ordinal
                    where c.Established != null
                    select c.Established).FirstOrDefault(),
                Dissolved = (
                    from c in mc.Contributions
                    orderby c.Ordinal
                    where c.Dissolved != null
                    select c.Dissolved).FirstOrDefault(),
                BirthDate = (
                    from c in mc.Contributions
                    orderby c.Ordinal
                    where c.BirthDate != null
                    select c.BirthDate).FirstOrDefault(),
                DeathDate = (
                    from c in mc.Contributions
                    orderby c.Ordinal
                    where c.DeathDate != null
                    select c.DeathDate).FirstOrDefault()
            };
    }

    /// <inheritdoc />
    public IQueryable<Guid> GetMatchingContainerIds(MetadataContributionCriteria criteria)
    {
        var query = GetMaterializedQuery();
        query = ApplyCriteria(query, criteria);
        return query.Select(m => m.ContainerId);
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<MaterializedMetadataResult>>> QueryMaterializedAsync(
        MetadataContributionCriteria criteria,
        PagingRequest? paging,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = GetMaterializedQuery();
            query = ApplyCriteria(query, criteria);
            query = query.OrderBy(m => m.ContainerId);

            var totalItems = await query.CountAsync(cancellationToken).ConfigureAwait(false);

            if (paging is not null)
            {
                query = query.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);
            }

            var projections = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

            var containerIds = projections.Select(p => p.ContainerId).ToList();
            var containers = await context.MetadataContainers
                .AsNoTracking()
                .Include(mc => mc.Contributions).ThenInclude(c => c.CustomMetadataValues)
                .Include(mc => mc.Contributions).ThenInclude(c => c.ExternalIds)
                .Include(mc => mc.Contributions).ThenInclude(c => c.References)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Relationships)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Images)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Previews)
                .Include(mc => mc.Contributions).ThenInclude(c => c.AlternateTexts)
                .Include(mc => mc.Contributions).ThenInclude(c => c.ContentRatings)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Awards)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Genres)
                .Where(mc => containerIds.Contains(mc.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var containerLookup = containers.ToDictionary(c => c.Id);
            var results = projections
                .Select(p => new MaterializedMetadataResult(
                    p.ContainerId,
                    containerLookup.TryGetValue(p.ContainerId, out var container)
                        ? MaterializeFromContainer(container)
                        : CreateEmptyMaterializedMetadata()))
                .ToImmutableList();

            return Result.Success(new PagedResult<MaterializedMetadataResult>(
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
                "Error querying materialized metadata ({ExErrorCode})",
                errorCode ?? "");
            return Result.Fail<PagedResult<MaterializedMetadataResult>>(
                "Error querying materialized metadata",
                ErrorCodes.DbMetadataError,
                errorCode);
        }
    }

    private IQueryable<MaterializedProjection> ApplyCriteria(
        IQueryable<MaterializedProjection> query,
        MetadataContributionCriteria criteria)
    {
        query = ApplyStringCriteria(query, criteria.Title, m => m.Title);
        query = ApplyStringCriteria(query, criteria.OriginalTitle, m => m.OriginalTitle);
        query = ApplyStringCriteria(query, criteria.SortTitle, m => m.SortTitle);
        query = ApplyStringCriteria(query, criteria.Language, m => m.Language);
        query = ApplyStringCriteria(query, criteria.Overview, m => m.Overview);
        query = ApplyStringCriteria(query, criteria.ProductionStatus, m => m.ProductionStatus);
        query = ApplyDateTimeCriteria(query, criteria.PublishDate, m => m.PublishDate);
        query = ApplyDateTimeCriteria(query, criteria.Established, m => m.Established);
        query = ApplyDateTimeCriteria(query, criteria.Dissolved, m => m.Dissolved);
        query = ApplyDateTimeCriteria(query, criteria.BirthDate, m => m.BirthDate);
        query = ApplyDateTimeCriteria(query, criteria.DeathDate, m => m.DeathDate);

        query = ApplyGenreCriteria(query, criteria.Genres);
        query = ApplyExternalIdCriteria(query, criteria.ExternalIds);
        query = ApplyImageCriteria(query, criteria.Images);
        query = ApplyAwardCriteria(query, criteria.Awards);
        query = ApplyReferenceCriteria(query, criteria.References);
        query = ApplyRelationshipCriteria(query, criteria.Relationships);
        query = ApplyPreviewCriteria(query, criteria.Previews);
        query = ApplyAlternateTextCriteria(query, criteria.AlternateTexts);
        query = ApplyContentRatingCriteria(query, criteria.ContentRatings);

        return query;
    }

    private static IQueryable<MaterializedProjection> ApplyStringCriteria(
        IQueryable<MaterializedProjection> query,
        AnyOfCriteria<FieldCriterion<string>> criteria,
        Expression<Func<MaterializedProjection, string?>> selector)
    {
        if (criteria.Any.Count == 0)
        {
            return query;
        }

        var predicates = new List<Expression<Func<MaterializedProjection, bool>>>();

        foreach (var criterion in criteria.Any)
        {
            var value = criterion.Value;
            switch (criterion.Operator)
            {
                case FieldOperator.Equals:
                    predicates.Add(selector.CombineWithPredicate(v => v == value));
                    break;
                case FieldOperator.NotEquals:
                    predicates.Add(selector.CombineWithPredicate(v => v != value));
                    break;
                case FieldOperator.IsNull:
                    predicates.Add(selector.CombineWithPredicate(v => v == null));
                    break;
                case FieldOperator.IsNotNull:
                    predicates.Add(selector.CombineWithPredicate(v => v != null));
                    break;
                case FieldOperator.ContainsString:
                    if (value is not null)
                    {
                        predicates.Add(selector.CombineWithPredicate(v => v != null && v.Contains(value)));
                    }
                    break;
                case FieldOperator.StartsWithString:
                    if (value is not null)
                    {
                        predicates.Add(selector.CombineWithPredicate(v => v != null && v.StartsWith(value)));
                    }
                    break;
                case FieldOperator.EndsWithString:
                    if (value is not null)
                    {
                        predicates.Add(selector.CombineWithPredicate(v => v != null && v.EndsWith(value)));
                    }
                    break;
                case FieldOperator.NotContainsString:
                    if (value is not null)
                    {
                        predicates.Add(selector.CombineWithPredicate(v => v == null || !v.Contains(value)));
                    }
                    break;
                case FieldOperator.NotStartsWithString:
                    if (value is not null)
                    {
                        predicates.Add(selector.CombineWithPredicate(v => v == null || !v.StartsWith(value)));
                    }
                    break;
                case FieldOperator.NotEndsWithString:
                    if (value is not null)
                    {
                        predicates.Add(selector.CombineWithPredicate(v => v == null || !v.EndsWith(value)));
                    }
                    break;
                case FieldOperator.In:
                    if (criterion.Values is { Count: > 0 })
                    {
                        var values = criterion.Values;
                        predicates.Add(selector.CombineWithPredicate(v => v != null && values.Contains(v)));
                    }
                    break;
                case FieldOperator.NotIn:
                    if (criterion.Values is { Count: > 0 })
                    {
                        var values = criterion.Values;
                        predicates.Add(selector.CombineWithPredicate(v => v == null || !values.Contains(v)));
                    }
                    break;
            }
        }

        if (predicates.Count == 0)
        {
            return query;
        }

        return query.Where(predicates.OrAll());
    }

    private static IQueryable<MaterializedProjection> ApplyDateTimeCriteria(
        IQueryable<MaterializedProjection> query,
        AnyOfCriteria<FieldCriterion<DateTime>> criteria,
        Expression<Func<MaterializedProjection, DateTime?>> selector)
    {
        if (criteria.Any.Count == 0)
        {
            return query;
        }

        var predicates = new List<Expression<Func<MaterializedProjection, bool>>>();

        foreach (var criterion in criteria.Any)
        {
            var value = criterion.Value;
            switch (criterion.Operator)
            {
                case FieldOperator.Equals:
                    predicates.Add(selector.CombineWithPredicate(v => v == value));
                    break;
                case FieldOperator.NotEquals:
                    predicates.Add(selector.CombineWithPredicate(v => v != value));
                    break;
                case FieldOperator.IsNull:
                    predicates.Add(selector.CombineWithPredicate(v => v == null));
                    break;
                case FieldOperator.IsNotNull:
                    predicates.Add(selector.CombineWithPredicate(v => v != null));
                    break;
                case FieldOperator.GreaterThan:
                    predicates.Add(selector.CombineWithPredicate(v => v > value));
                    break;
                case FieldOperator.GreaterThanOrEqual:
                    predicates.Add(selector.CombineWithPredicate(v => v >= value));
                    break;
                case FieldOperator.LessThan:
                    predicates.Add(selector.CombineWithPredicate(v => v < value));
                    break;
                case FieldOperator.LessThanOrEqual:
                    predicates.Add(selector.CombineWithPredicate(v => v <= value));
                    break;
                case FieldOperator.Between:
                    if (criterion.Range is not null)
                    {
                        var min = criterion.Range.Min;
                        var max = criterion.Range.Max;
                        predicates.Add(selector.CombineWithPredicate(v => v >= min && v <= max));
                    }
                    break;
                case FieldOperator.NotBetween:
                    if (criterion.Range is not null)
                    {
                        var min = criterion.Range.Min;
                        var max = criterion.Range.Max;
                        predicates.Add(selector.CombineWithPredicate(v => v < min || v > max));
                    }
                    break;
            }
        }

        if (predicates.Count == 0)
        {
            return query;
        }

        return query.Where(predicates.OrAll());
    }

    private IQueryable<MaterializedProjection> ApplyGenreCriteria(
        IQueryable<MaterializedProjection> query,
        CollectionCriteria<GenreMetadataCriteria> criteria)
    {
        if (criteria.Criteria.Count == 0)
        {
            return query;
        }

        var matchingContainerIds = GetContainerIdsMatchingGenreCriteria(criteria);
        return query.Where(m => matchingContainerIds.Contains(m.ContainerId));
    }

    private IQueryable<Guid> GetContainerIdsMatchingGenreCriteria(
        CollectionCriteria<GenreMetadataCriteria> criteria)
    {
        var genreValues = ExtractGenreValues(criteria.Criteria);
        var baseQuery = context.MetadataContainers.AsNoTracking();

        return criteria.Operator switch
        {
            CollectionOperator.Any => baseQuery
                .Where(mc => mc.Contributions.SelectMany(c => c.Genres)
                    .Any(g => genreValues.Contains(g.GenreName)))
                .Select(mc => mc.Id),
            CollectionOperator.All => GetContainerIdsMatchingAllGenres(baseQuery, criteria.Criteria),
            CollectionOperator.None => baseQuery
                .Where(mc => !mc.Contributions.SelectMany(c => c.Genres)
                    .Any(g => genreValues.Contains(g.GenreName)))
                .Select(mc => mc.Id),
            _ => baseQuery.Select(mc => mc.Id)
        };
    }

    private IQueryable<Guid> GetContainerIdsMatchingAllGenres(
        IQueryable<MetadataContainerEntity> baseQuery,
        IReadOnlyList<GenreMetadataCriteria> criteriaList)
    {
        var query = baseQuery;
        foreach (var criterion in criteriaList)
        {
            var genreValues = ExtractSingleCriterionGenreValues(criterion);
            query = query.Where(mc => mc.Contributions.SelectMany(c => c.Genres)
                .Any(g => genreValues.Contains(g.GenreName)));
        }
        return query.Select(mc => mc.Id);
    }

    private static List<string> ExtractGenreValues(IReadOnlyList<GenreMetadataCriteria> criteriaList)
    {
        var values = new List<string>();
        foreach (var criterion in criteriaList)
        {
            values.AddRange(ExtractSingleCriterionGenreValues(criterion));
        }
        return values;
    }

    private static List<string> ExtractSingleCriterionGenreValues(GenreMetadataCriteria criterion)
    {
        var values = new List<string>();
        foreach (var fieldCriterion in criterion.Genre.Any)
        {
            if (fieldCriterion.Operator == FieldOperator.Equals && fieldCriterion.Value != null)
            {
                values.Add(fieldCriterion.Value);
            }
            else if (fieldCriterion.Operator == FieldOperator.In && fieldCriterion.Values != null)
            {
                values.AddRange(fieldCriterion.Values.Where(v => v != null)!);
            }
        }
        return values;
    }

    private IQueryable<MaterializedProjection> ApplyExternalIdCriteria(
        IQueryable<MaterializedProjection> query,
        CollectionCriteria<ExternalIdMetadataCriteria> criteria)
    {
        if (criteria.Criteria.Count == 0)
        {
            return query;
        }

        var matchingContainerIds = GetContainerIdsMatchingExternalIdCriteria(criteria);
        return query.Where(m => matchingContainerIds.Contains(m.ContainerId));
    }

    private IQueryable<Guid> GetContainerIdsMatchingExternalIdCriteria(
        CollectionCriteria<ExternalIdMetadataCriteria> criteria)
    {
        var attributions = ExtractStringValues([.. criteria.Criteria.SelectMany(c => c.Attribution.Any)]);
        var values = ExtractStringValues([.. criteria.Criteria.SelectMany(c => c.Value.Any)]);
        var baseQuery = context.MetadataContainers.AsNoTracking();

        var hasAttributions = attributions.Count > 0;
        var hasValues = values.Count > 0;

        return criteria.Operator switch
        {
            CollectionOperator.Any => baseQuery
                .Where(mc => mc.Contributions.SelectMany(c => c.ExternalIds)
                    .Any(e => (!hasAttributions || attributions.Contains(e.Attribution!))
                        && (!hasValues || values.Contains(e.Value))))
                .Select(mc => mc.Id),
            CollectionOperator.All => GetContainerIdsMatchingAllExternalIds(baseQuery, criteria.Criteria),
            CollectionOperator.None => baseQuery
                .Where(mc => !mc.Contributions.SelectMany(c => c.ExternalIds)
                    .Any(e => (!hasAttributions || attributions.Contains(e.Attribution!))
                        && (!hasValues || values.Contains(e.Value))))
                .Select(mc => mc.Id),
            _ => baseQuery.Select(mc => mc.Id)
        };
    }

    private IQueryable<Guid> GetContainerIdsMatchingAllExternalIds(
        IQueryable<MetadataContainerEntity> baseQuery,
        IReadOnlyList<ExternalIdMetadataCriteria> criteriaList)
    {
        var query = baseQuery;
        foreach (var criterion in criteriaList)
        {
            var attributions = ExtractStringValues([.. criterion.Attribution.Any]);
            var values = ExtractStringValues([.. criterion.Value.Any]);
            var hasAttributions = attributions.Count > 0;
            var hasValues = values.Count > 0;

            query = query.Where(mc => mc.Contributions.SelectMany(c => c.ExternalIds)
                .Any(e => (!hasAttributions || attributions.Contains(e.Attribution!))
                    && (!hasValues || values.Contains(e.Value))));
        }
        return query.Select(mc => mc.Id);
    }

    private IQueryable<MaterializedProjection> ApplyImageCriteria(
        IQueryable<MaterializedProjection> query,
        CollectionCriteria<ImageMetadataCriteria> criteria)
    {
        if (criteria.Criteria.Count == 0)
        {
            return query;
        }

        var matchingContainerIds = GetContainerIdsMatchingImageCriteria(criteria);
        return query.Where(m => matchingContainerIds.Contains(m.ContainerId));
    }

    private IQueryable<Guid> GetContainerIdsMatchingImageCriteria(
        CollectionCriteria<ImageMetadataCriteria> criteria)
    {
        var imageTypes = ExtractStringValues([.. criteria.Criteria.SelectMany(c => c.ImageType.Any)]);
        var baseQuery = context.MetadataContainers.AsNoTracking();
        var hasImageTypes = imageTypes.Count > 0;

        return criteria.Operator switch
        {
            CollectionOperator.Any => baseQuery
                .Where(mc => mc.Contributions.SelectMany(c => c.Images)
                    .Any(i => !hasImageTypes || imageTypes.Contains(i.ImageType)))
                .Select(mc => mc.Id),
            CollectionOperator.All => GetContainerIdsMatchingAllImages(baseQuery, criteria.Criteria),
            CollectionOperator.None => baseQuery
                .Where(mc => !mc.Contributions.SelectMany(c => c.Images)
                    .Any(i => !hasImageTypes || imageTypes.Contains(i.ImageType)))
                .Select(mc => mc.Id),
            _ => baseQuery.Select(mc => mc.Id)
        };
    }

    private IQueryable<Guid> GetContainerIdsMatchingAllImages(
        IQueryable<MetadataContainerEntity> baseQuery,
        IReadOnlyList<ImageMetadataCriteria> criteriaList)
    {
        var query = baseQuery;
        foreach (var criterion in criteriaList)
        {
            var imageTypes = ExtractStringValues([.. criterion.ImageType.Any]);
            var hasImageTypes = imageTypes.Count > 0;

            query = query.Where(mc => mc.Contributions.SelectMany(c => c.Images)
                .Any(i => !hasImageTypes || imageTypes.Contains(i.ImageType)));
        }
        return query.Select(mc => mc.Id);
    }

    private IQueryable<MaterializedProjection> ApplyAwardCriteria(
        IQueryable<MaterializedProjection> query,
        CollectionCriteria<AwardMetadataCriteria> criteria)
    {
        if (criteria.Criteria.Count == 0)
        {
            return query;
        }

        var matchingContainerIds = GetContainerIdsMatchingAwardCriteria(criteria);
        return query.Where(m => matchingContainerIds.Contains(m.ContainerId));
    }

    private IQueryable<Guid> GetContainerIdsMatchingAwardCriteria(
        CollectionCriteria<AwardMetadataCriteria> criteria)
    {
        var awardBodies = ExtractStringValues([.. criteria.Criteria.SelectMany(c => c.AwardBody.Any)]);
        var baseQuery = context.MetadataContainers.AsNoTracking();
        var hasAwardBodies = awardBodies.Count > 0;

        return criteria.Operator switch
        {
            CollectionOperator.Any => baseQuery
                .Where(mc => mc.Contributions.SelectMany(c => c.Awards)
                    .Any(a => !hasAwardBodies || awardBodies.Contains(a.AwardBody)))
                .Select(mc => mc.Id),
            CollectionOperator.All => GetContainerIdsMatchingAllAwards(baseQuery, criteria.Criteria),
            CollectionOperator.None => baseQuery
                .Where(mc => !mc.Contributions.SelectMany(c => c.Awards)
                    .Any(a => !hasAwardBodies || awardBodies.Contains(a.AwardBody)))
                .Select(mc => mc.Id),
            _ => baseQuery.Select(mc => mc.Id)
        };
    }

    private IQueryable<Guid> GetContainerIdsMatchingAllAwards(
        IQueryable<MetadataContainerEntity> baseQuery,
        IReadOnlyList<AwardMetadataCriteria> criteriaList)
    {
        var query = baseQuery;
        foreach (var criterion in criteriaList)
        {
            var awardBodies = ExtractStringValues([.. criterion.AwardBody.Any]);
            var hasAwardBodies = awardBodies.Count > 0;

            query = query.Where(mc => mc.Contributions.SelectMany(c => c.Awards)
                .Any(a => !hasAwardBodies || awardBodies.Contains(a.AwardBody)));
        }
        return query.Select(mc => mc.Id);
    }

    private IQueryable<MaterializedProjection> ApplyReferenceCriteria(
        IQueryable<MaterializedProjection> query,
        CollectionCriteria<ReferenceMetadataCriteria> criteria)
    {
        if (criteria.Criteria.Count == 0)
        {
            return query;
        }

        var matchingContainerIds = GetContainerIdsMatchingReferenceCriteria(criteria);
        return query.Where(m => matchingContainerIds.Contains(m.ContainerId));
    }

    private IQueryable<Guid> GetContainerIdsMatchingReferenceCriteria(
        CollectionCriteria<ReferenceMetadataCriteria> criteria)
    {
        var kinds = ExtractStringValues([.. criteria.Criteria.SelectMany(c => c.Kind.Any)]);
        var baseQuery = context.MetadataContainers.AsNoTracking();
        var hasKinds = kinds.Count > 0;

        return criteria.Operator switch
        {
            CollectionOperator.Any => baseQuery
                .Where(mc => mc.Contributions.SelectMany(c => c.References)
                    .Any(r => !hasKinds || kinds.Contains(r.Kind)))
                .Select(mc => mc.Id),
            CollectionOperator.All => GetContainerIdsMatchingAllReferences(baseQuery, criteria.Criteria),
            CollectionOperator.None => baseQuery
                .Where(mc => !mc.Contributions.SelectMany(c => c.References)
                    .Any(r => !hasKinds || kinds.Contains(r.Kind)))
                .Select(mc => mc.Id),
            _ => baseQuery.Select(mc => mc.Id)
        };
    }

    private IQueryable<Guid> GetContainerIdsMatchingAllReferences(
        IQueryable<MetadataContainerEntity> baseQuery,
        IReadOnlyList<ReferenceMetadataCriteria> criteriaList)
    {
        var query = baseQuery;
        foreach (var criterion in criteriaList)
        {
            var kinds = ExtractStringValues([.. criterion.Kind.Any]);
            var hasKinds = kinds.Count > 0;

            query = query.Where(mc => mc.Contributions.SelectMany(c => c.References)
                .Any(r => !hasKinds || kinds.Contains(r.Kind)));
        }
        return query.Select(mc => mc.Id);
    }

    private IQueryable<MaterializedProjection> ApplyRelationshipCriteria(
        IQueryable<MaterializedProjection> query,
        CollectionCriteria<RelationshipMetadataCriteria> criteria)
    {
        if (criteria.Criteria.Count == 0)
        {
            return query;
        }

        var matchingContainerIds = GetContainerIdsMatchingRelationshipCriteria(criteria);
        return query.Where(m => matchingContainerIds.Contains(m.ContainerId));
    }

    private IQueryable<Guid> GetContainerIdsMatchingRelationshipCriteria(
        CollectionCriteria<RelationshipMetadataCriteria> criteria)
    {
        var relations = ExtractStringValues([.. criteria.Criteria.SelectMany(c => c.Relation.Any)]);
        var baseQuery = context.MetadataContainers.AsNoTracking();
        var hasRelations = relations.Count > 0;

        return criteria.Operator switch
        {
            CollectionOperator.Any => baseQuery
                .Where(mc => mc.Contributions.SelectMany(c => c.Relationships)
                    .Any(r => !hasRelations || relations.Contains(r.Relation)))
                .Select(mc => mc.Id),
            CollectionOperator.All => GetContainerIdsMatchingAllRelationships(baseQuery, criteria.Criteria),
            CollectionOperator.None => baseQuery
                .Where(mc => !mc.Contributions.SelectMany(c => c.Relationships)
                    .Any(r => !hasRelations || relations.Contains(r.Relation)))
                .Select(mc => mc.Id),
            _ => baseQuery.Select(mc => mc.Id)
        };
    }

    private IQueryable<Guid> GetContainerIdsMatchingAllRelationships(
        IQueryable<MetadataContainerEntity> baseQuery,
        IReadOnlyList<RelationshipMetadataCriteria> criteriaList)
    {
        var query = baseQuery;
        foreach (var criterion in criteriaList)
        {
            var relations = ExtractStringValues([.. criterion.Relation.Any]);
            var hasRelations = relations.Count > 0;

            query = query.Where(mc => mc.Contributions.SelectMany(c => c.Relationships)
                .Any(r => !hasRelations || relations.Contains(r.Relation)));
        }
        return query.Select(mc => mc.Id);
    }

    private IQueryable<MaterializedProjection> ApplyPreviewCriteria(
        IQueryable<MaterializedProjection> query,
        CollectionCriteria<PreviewMetadataCriteria> criteria)
    {
        if (criteria.Criteria.Count == 0)
        {
            return query;
        }

        var matchingContainerIds = GetContainerIdsMatchingPreviewCriteria(criteria);
        return query.Where(m => matchingContainerIds.Contains(m.ContainerId));
    }

    private IQueryable<Guid> GetContainerIdsMatchingPreviewCriteria(
        CollectionCriteria<PreviewMetadataCriteria> criteria)
    {
        var previewTypes = ExtractStringValues([.. criteria.Criteria.SelectMany(c => c.PreviewType.Any)]);
        var baseQuery = context.MetadataContainers.AsNoTracking();
        var hasPreviewTypes = previewTypes.Count > 0;

        return criteria.Operator switch
        {
            CollectionOperator.Any => baseQuery
                .Where(mc => mc.Contributions.SelectMany(c => c.Previews)
                    .Any(p => !hasPreviewTypes || previewTypes.Contains(p.PreviewType)))
                .Select(mc => mc.Id),
            CollectionOperator.All => GetContainerIdsMatchingAllPreviews(baseQuery, criteria.Criteria),
            CollectionOperator.None => baseQuery
                .Where(mc => !mc.Contributions.SelectMany(c => c.Previews)
                    .Any(p => !hasPreviewTypes || previewTypes.Contains(p.PreviewType)))
                .Select(mc => mc.Id),
            _ => baseQuery.Select(mc => mc.Id)
        };
    }

    private IQueryable<Guid> GetContainerIdsMatchingAllPreviews(
        IQueryable<MetadataContainerEntity> baseQuery,
        IReadOnlyList<PreviewMetadataCriteria> criteriaList)
    {
        var query = baseQuery;
        foreach (var criterion in criteriaList)
        {
            var previewTypes = ExtractStringValues([.. criterion.PreviewType.Any]);
            var hasPreviewTypes = previewTypes.Count > 0;

            query = query.Where(mc => mc.Contributions.SelectMany(c => c.Previews)
                .Any(p => !hasPreviewTypes || previewTypes.Contains(p.PreviewType)));
        }
        return query.Select(mc => mc.Id);
    }

    private IQueryable<MaterializedProjection> ApplyAlternateTextCriteria(
        IQueryable<MaterializedProjection> query,
        CollectionCriteria<AlternateTextMetadataCriteria> criteria)
    {
        if (criteria.Criteria.Count == 0)
        {
            return query;
        }

        var matchingContainerIds = GetContainerIdsMatchingAlternateTextCriteria(criteria);
        return query.Where(m => matchingContainerIds.Contains(m.ContainerId));
    }

    private IQueryable<Guid> GetContainerIdsMatchingAlternateTextCriteria(
        CollectionCriteria<AlternateTextMetadataCriteria> criteria)
    {
        var types = ExtractStringValues([.. criteria.Criteria.SelectMany(c => c.Type.Any)]);
        var baseQuery = context.MetadataContainers.AsNoTracking();
        var hasTypes = types.Count > 0;

        return criteria.Operator switch
        {
            CollectionOperator.Any => baseQuery
                .Where(mc => mc.Contributions.SelectMany(c => c.AlternateTexts)
                    .Any(a => !hasTypes || types.Contains(a.Type)))
                .Select(mc => mc.Id),
            CollectionOperator.All => GetContainerIdsMatchingAllAlternateTexts(baseQuery, criteria.Criteria),
            CollectionOperator.None => baseQuery
                .Where(mc => !mc.Contributions.SelectMany(c => c.AlternateTexts)
                    .Any(a => !hasTypes || types.Contains(a.Type)))
                .Select(mc => mc.Id),
            _ => baseQuery.Select(mc => mc.Id)
        };
    }

    private IQueryable<Guid> GetContainerIdsMatchingAllAlternateTexts(
        IQueryable<MetadataContainerEntity> baseQuery,
        IReadOnlyList<AlternateTextMetadataCriteria> criteriaList)
    {
        var query = baseQuery;
        foreach (var criterion in criteriaList)
        {
            var types = ExtractStringValues([.. criterion.Type.Any]);
            var hasTypes = types.Count > 0;

            query = query.Where(mc => mc.Contributions.SelectMany(c => c.AlternateTexts)
                .Any(a => !hasTypes || types.Contains(a.Type)));
        }
        return query.Select(mc => mc.Id);
    }

    private IQueryable<MaterializedProjection> ApplyContentRatingCriteria(
        IQueryable<MaterializedProjection> query,
        CollectionCriteria<ContentRatingMetadataCriteria> criteria)
    {
        if (criteria.Criteria.Count == 0)
        {
            return query;
        }

        var matchingContainerIds = GetContainerIdsMatchingContentRatingCriteria(criteria);
        return query.Where(m => matchingContainerIds.Contains(m.ContainerId));
    }

    private IQueryable<Guid> GetContainerIdsMatchingContentRatingCriteria(
        CollectionCriteria<ContentRatingMetadataCriteria> criteria)
    {
        var ratingsValues = ExtractStringValues([.. criteria.Criteria.SelectMany(c => c.Value.Any)]);
        var baseQuery = context.MetadataContainers.AsNoTracking();
        var hasValues = ratingsValues.Count > 0;

        return criteria.Operator switch
        {
            CollectionOperator.Any => baseQuery
                .Where(mc => mc.Contributions.SelectMany(c => c.ContentRatings)
                    .Any(cr => !hasValues || ratingsValues.Contains(cr.Value)))
                .Select(mc => mc.Id),
            CollectionOperator.All => GetContainerIdsMatchingAllContentRatings(baseQuery, criteria.Criteria),
            CollectionOperator.None => baseQuery
                .Where(mc => !mc.Contributions.SelectMany(c => c.ContentRatings)
                    .Any(cr => !hasValues || ratingsValues.Contains(cr.Value)))
                .Select(mc => mc.Id),
            _ => baseQuery.Select(mc => mc.Id)
        };
    }

    private IQueryable<Guid> GetContainerIdsMatchingAllContentRatings(
        IQueryable<MetadataContainerEntity> baseQuery,
        IReadOnlyList<ContentRatingMetadataCriteria> criteriaList)
    {
        var query = baseQuery;
        foreach (var criterion in criteriaList)
        {
            var ratingsValues = ExtractStringValues([.. criterion.Value.Any]);
            var hasValues = ratingsValues.Count > 0;

            query = query.Where(mc => mc.Contributions.SelectMany(c => c.ContentRatings)
                .Any(cr => !hasValues || ratingsValues.Contains(cr.Value)));
        }
        return query.Select(mc => mc.Id);
    }

    private static List<string> ExtractStringValues(IReadOnlyList<FieldCriterion<string>> criteria)
    {
        var values = new List<string>();
        foreach (var criterion in criteria)
        {
            if (criterion.Operator == FieldOperator.Equals && criterion.Value != null)
            {
                values.Add(criterion.Value);
            }
            else if (criterion.Operator == FieldOperator.In && criterion.Values != null)
            {
                values.AddRange(criterion.Values.Where(v => v != null)!);
            }
        }
        return values;
    }

    private static MaterializedMetadata MaterializeFromContainer(MetadataContainerEntity container)
    {
        var orderedContributions = container.Contributions.OrderBy(c => c.Ordinal).ToList();

        if (orderedContributions.Count == 0)
        {
            return CreateEmptyMaterializedMetadata();
        }

        var title = orderedContributions.Select(c => c.Title).FirstOrDefault(v => v is not null);
        var originalTitle = orderedContributions.Select(c => c.OriginalTitle).FirstOrDefault(v => v is not null);
        var sortTitle = orderedContributions.Select(c => c.SortTitle).FirstOrDefault(v => v is not null);
        var language = orderedContributions.Select(c => c.Language).FirstOrDefault(v => v is not null);
        var overview = orderedContributions.Select(c => c.Overview).FirstOrDefault(v => v is not null);
        var publishDate = orderedContributions.Select(c => c.PublishDate).FirstOrDefault(v => v is not null);
        var duration = orderedContributions.Select(c => c.Duration).FirstOrDefault(v => v is not null);
        var productionStatus = orderedContributions.Select(c => c.ProductionStatus).FirstOrDefault(v => v is not null);
        var established = orderedContributions.Select(c => c.Established).FirstOrDefault(v => v is not null);
        var dissolved = orderedContributions.Select(c => c.Dissolved).FirstOrDefault(v => v is not null);
        var birthDate = orderedContributions.Select(c => c.BirthDate).FirstOrDefault(v => v is not null);
        var deathDate = orderedContributions.Select(c => c.DeathDate).FirstOrDefault(v => v is not null);

        var customMetadata = MergeCustomMetadata(orderedContributions);
        var externalIds = MergeExternalIds(orderedContributions);
        var references = MergeReferences(orderedContributions);
        var relationships = MergeRelationships(orderedContributions);
        var images = MergeImages(orderedContributions);
        var previews = MergePreviews(orderedContributions);
        var alternateTexts = MergeAlternateTexts(orderedContributions);
        var contentRatings = MergeContentRatings(orderedContributions);
        var awards = MergeAwards(orderedContributions);
        var genres = MergeGenres(orderedContributions);

        return new(
            title,
            originalTitle,
            sortTitle,
            language,
            overview,
            publishDate,
            duration,
            productionStatus,
            established,
            dissolved,
            birthDate,
            deathDate,
            customMetadata,
            externalIds,
            references,
            relationships,
            images,
            previews,
            alternateTexts,
            contentRatings,
            awards,
            genres);
    }

    private static MaterializedMetadata CreateEmptyMaterializedMetadata()
    {
        return new(
            null, null, null, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, null, null, null);
    }

    private static IReadOnlyDictionary<string, string?>? MergeCustomMetadata(
        List<MetadataContributionEntity> contributions)
    {
        var merged = new Dictionary<string, string?>();
        foreach (var contribution in contributions)
        {
            foreach (var kv in contribution.CustomMetadataValues)
            {
                merged.TryAdd(kv.Key, kv.Value);
            }
        }

        return merged.Count > 0 ? merged.ToImmutableDictionary() : null;
    }

    private static IReadOnlyList<ExternalIdMetadata>? MergeExternalIds(
        List<MetadataContributionEntity> contributions)
    {
        var seen = new HashSet<string>();
        var merged = new List<ExternalIdMetadata>();

        foreach (var contribution in contributions)
        {
            foreach (var item in contribution.ExternalIds.OrderBy(e => e.Ordinal))
            {
                var key = $"{item.Attribution}:{item.Value}";
                if (seen.Add(key))
                {
                    merged.Add(item.ToModel());
                }
            }
        }

        return merged.Count > 0 ? merged.ToImmutableList() : null;
    }

    private static IReadOnlyList<ReferenceMetadata>? MergeReferences(
        List<MetadataContributionEntity> contributions)
    {
        var seen = new HashSet<string>();
        var merged = new List<ReferenceMetadata>();

        foreach (var contribution in contributions)
        {
            foreach (var item in contribution.References.OrderBy(r => r.Ordinal))
            {
                var key = item.Url.ToString();
                if (seen.Add(key))
                {
                    merged.Add(item.ToModel());
                }
            }
        }

        return merged.Count > 0 ? merged.ToImmutableList() : null;
    }

    private static IReadOnlyList<RelationshipMetadata>? MergeRelationships(
        List<MetadataContributionEntity> contributions)
    {
        var seen = new HashSet<string>();
        var merged = new List<RelationshipMetadata>();

        foreach (var contribution in contributions)
        {
            foreach (var item in contribution.Relationships.OrderBy(r => r.Ordinal))
            {
                var key = $"{item.SubjectId}:{item.Relation}:{item.Qualifier}";
                if (seen.Add(key))
                {
                    merged.Add(item.ToModel());
                }
            }
        }

        return merged.Count > 0 ? merged.ToImmutableList() : null;
    }

    private static IReadOnlyList<ImageMetadata>? MergeImages(
        List<MetadataContributionEntity> contributions)
    {
        var seen = new HashSet<string>();
        var merged = new List<ImageMetadata>();

        foreach (var contribution in contributions)
        {
            foreach (var item in contribution.Images.OrderBy(i => i.Ordinal))
            {
                var key = item.Url.ToString();
                if (seen.Add(key))
                {
                    merged.Add(item.ToModel());
                }
            }
        }

        return merged.Count > 0 ? merged.ToImmutableList() : null;
    }

    private static IReadOnlyList<PreviewMetadata>? MergePreviews(
        List<MetadataContributionEntity> contributions)
    {
        var seen = new HashSet<string>();
        var merged = new List<PreviewMetadata>();

        foreach (var contribution in contributions)
        {
            foreach (var item in contribution.Previews.OrderBy(p => p.Ordinal))
            {
                var key = item.Url.ToString();
                if (seen.Add(key))
                {
                    merged.Add(item.ToModel());
                }
            }
        }

        return merged.Count > 0 ? merged.ToImmutableList() : null;
    }

    private static IReadOnlyList<AlternateTextMetadata>? MergeAlternateTexts(
        List<MetadataContributionEntity> contributions)
    {
        var seen = new HashSet<string>();
        var merged = new List<AlternateTextMetadata>();

        foreach (var contribution in contributions)
        {
            foreach (var item in contribution.AlternateTexts.OrderBy(a => a.Ordinal))
            {
                var key = $"{item.Type}:{item.Language}:{item.Text}";
                if (seen.Add(key))
                {
                    merged.Add(item.ToModel());
                }
            }
        }

        return merged.Count > 0 ? merged.ToImmutableList() : null;
    }

    private static IReadOnlyList<ContentRatingMetadata>? MergeContentRatings(
        List<MetadataContributionEntity> contributions)
    {
        var seen = new HashSet<string>();
        var merged = new List<ContentRatingMetadata>();

        foreach (var contribution in contributions)
        {
            foreach (var item in contribution.ContentRatings.OrderBy(c => c.Ordinal))
            {
                var key = $"{item.RatingsBody}:{item.Value}";
                if (seen.Add(key))
                {
                    merged.Add(item.ToModel());
                }
            }
        }

        return merged.Count > 0 ? merged.ToImmutableList() : null;
    }

    private static IReadOnlyList<AwardMetadata>? MergeAwards(
        List<MetadataContributionEntity> contributions)
    {
        var seen = new HashSet<string>();
        var merged = new List<AwardMetadata>();

        foreach (var contribution in contributions)
        {
            foreach (var item in contribution.Awards.OrderBy(a => a.Ordinal))
            {
                var key = $"{item.AwardBody}:{item.Name}:{item.Category}:{item.Year}";
                if (seen.Add(key))
                {
                    merged.Add(item.ToModel());
                }
            }
        }

        return merged.Count > 0 ? merged.ToImmutableList() : null;
    }

    private static IReadOnlyList<GenreMetadata>? MergeGenres(
        List<MetadataContributionEntity> contributions)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<GenreMetadata>();

        foreach (var contribution in contributions)
        {
            foreach (var item in contribution.Genres.OrderBy(g => g.Ordinal))
            {
                if (seen.Add(item.GenreName))
                {
                    merged.Add(item.ToModel());
                }
            }
        }

        return merged.Count > 0 ? merged.ToImmutableList() : null;
    }
}
