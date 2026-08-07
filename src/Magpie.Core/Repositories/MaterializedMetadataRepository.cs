using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Paging;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class MaterializedMetadataRepository(
    MagpieDbContext context,
    ILogger<MaterializedMetadataRepository> logger) : IMaterializedMetadataRepository
{
    private static readonly HashSet<string> AllowedMaterializedMetadataSortProperties = [
        nameof(MaterializedMetadata.SortTitle),
        nameof(MaterializedMetadata.Language),
        nameof(MaterializedMetadata.Overview),
        nameof(MaterializedMetadata.PublishDate),
        nameof(MaterializedMetadata.Duration),
        nameof(MaterializedMetadata.SequenceNumber),
        nameof(MaterializedMetadata.ProductionStatus),
        nameof(MaterializedMetadata.Established),
        nameof(MaterializedMetadata.Dissolved),
        nameof(MaterializedMetadata.BirthDate),
        nameof(MaterializedMetadata.DeathDate),
    ];

    private static Expression<Func<T, bool>> ApplyTypePredicate<T>(
        PropertyCriterion<string> criterion,
        Expression<Func<T, string?>> selector)
    {
        var value = criterion.Value?.ToLower();

        Expression<Func<string?, bool>> test = criterion.Operator switch
        {
            CriteriaValueOperator.Equals => v => v != null && v.ToLower() == value,
            CriteriaValueOperator.NotEquals => v => v == null || v.ToLower() != value,
            CriteriaValueOperator.ContainsString => v => value != null && v != null && v.ToLower().Contains(value),
            CriteriaValueOperator.NotContainsString => v => value == null || v == null || !v.ToLower().Contains(value),
            CriteriaValueOperator.StartsWithString => v => value != null && v != null && v.ToLower().StartsWith(value),
            CriteriaValueOperator.EndsWithString => v => value != null && v != null && v.ToLower().EndsWith(value),
            var op => throw new NotSupportedException($"Operator {op} is not supported for relationship type")
        };

        var parameter = selector.Parameters[0];
        var body = ReplacingExpressionVisitor.Replace(test.Parameters[0], selector.Body, test.Body);
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static bool IsNegativeSubjectOperator(CriteriaValueOperator op) =>
        op is CriteriaValueOperator.NotContainsString or CriteriaValueOperator.NotEquals;

    private static PropertyCriterion<string> ToInnerPositive(PropertyCriterion<string> criterion) =>
        criterion.Operator switch
        {
            CriteriaValueOperator.NotContainsString =>
                criterion with { Operator = CriteriaValueOperator.ContainsString },
            CriteriaValueOperator.NotEquals => criterion with { Operator = CriteriaValueOperator.Equals },
            _ => criterion,
        };

    private static IQueryable<T> ApplyStringPredicate<T>(
        IQueryable<T> query,
        PropertyCriterion<string> criterion,
        Expression<Func<T, string?>> selector)
    {
        var value = criterion.Value?.ToLower();
        Expression<Func<string?, bool>> test = criterion.Operator switch
        {
            CriteriaValueOperator.Equals => v => v != null && v.ToLower() == value,
            CriteriaValueOperator.NotEquals => v => v == null || v.ToLower() != value,
            CriteriaValueOperator.ContainsString => v => value != null && v != null && v.ToLower().Contains(value),
            CriteriaValueOperator.NotContainsString => v => value == null || v == null || !v.ToLower().Contains(value),
            CriteriaValueOperator.StartsWithString => v => value != null && v != null && v.ToLower().StartsWith(value),
            CriteriaValueOperator.EndsWithString => v => value != null && v != null && v.ToLower().EndsWith(value),
            var op => throw new NotSupportedException($"Operator {op} is not supported for relationship subject")
        };

        var parameter = selector.Parameters[0];
        var body = ReplacingExpressionVisitor.Replace(test.Parameters[0], selector.Body, test.Body);
        return query.Where(Expression.Lambda<Func<T, bool>>(body, parameter));
    }

    private static IQueryable<Relationship> ApplyStringRelationshipPredicate(
        IQueryable<Relationship> query,
        PropertyCriterion<string> criterion,
        Expression<Func<Relationship, string?>> selector)
    {
        var value = criterion.Value?.ToLower();
        return criterion.Operator switch
        {
            CriteriaValueOperator.Equals => query.Where(
                BuildRelationshipComparison(selector, v => v != null && v.ToLower() == value)),
            CriteriaValueOperator.NotEquals => query.Where(
                BuildRelationshipComparison(selector, v => v == null || v.ToLower() != value)),
            CriteriaValueOperator.ContainsString => query.Where(
                BuildRelationshipComparison(selector, v => value != null && v != null && v.ToLower().Contains(value))),
            CriteriaValueOperator.NotContainsString => query.Where(BuildRelationshipComparison(
                selector, v => value == null || v == null || !v.ToLower().Contains(value))),
            CriteriaValueOperator.StartsWithString => query.Where(BuildRelationshipComparison(
                selector, v => value != null && v != null && v.ToLower().StartsWith(value))),
            CriteriaValueOperator.EndsWithString => query.Where(
                BuildRelationshipComparison(selector, v => value != null && v != null && v.ToLower().EndsWith(value))),
            CriteriaValueOperator.IsNull => query.Where(BuildRelationshipComparison(selector, v => v == null)),
            CriteriaValueOperator.IsNotNull => query.Where(BuildRelationshipComparison(selector, v => v != null)),
            var op => throw new NotSupportedException($"Operator {op} is not supported for relationship fields")
        };
    }

    private static Expression<Func<Relationship, bool>> BuildRelationshipComparison(
        Expression<Func<Relationship, string?>> selector,
        Expression<Func<string?, bool>> test)
    {
        var parameter = selector.Parameters[0];
        var body = ReplacingExpressionVisitor.Replace(test.Parameters[0], selector.Body, test.Body);
        return Expression.Lambda<Func<Relationship, bool>>(body, parameter);
    }

    private static IQueryable<MaterializedMetadata> ApplySort(
        IQueryable<MaterializedMetadata> query,
        SortParameter? sort)
    {
        if (sort is null)
        {
            return query.OrderBy(m => m.ContainerId);
        }

        var sortProperty = typeof(MaterializedMetadata).GetProperties()
            .Select(property => property.Name)
            .FirstOrDefault(name => string.Equals(name, sort.PropertyName, StringComparison.OrdinalIgnoreCase));
        if (sortProperty is null || !AllowedMaterializedMetadataSortProperties.Contains(sortProperty))
        {
            return query.OrderBy(m => m.ContainerId);
        }

        return sort.Direction == SortDirection.Ascending
            ? query.OrderBy(m => EF.Property<MaterializedMetadata>(m, sortProperty)).ThenBy(m => m.ContainerId)
            : query.OrderByDescending(m => EF.Property<MaterializedMetadata>(m, sortProperty))
                .ThenBy(m => m.ContainerId);
    }

    public async Task<IEnumerable<MaterializedMetadata>> ListAllAsync(CancellationToken cancellationToken)
    {
        return await GetMetadataProjection()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PagedList<MaterializedMetadata>> QueryAsync(
        MetadataCriteria query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projection = ApplyCriteria(GetMetadataProjection(query.PreferredLanguage), query);
            var totalItems = await projection.CountAsync(cancellationToken).ConfigureAwait(false);

            projection = ApplySort(projection, sort);

            projection = projection
                .Skip((paging.PageNumber - 1) * paging.PageSize)
                .Take(paging.PageSize);

            var materialized = await projection.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = materialized,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalItems = totalItems
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The materialized metadata repository failed to query materialized metadata.");
            throw;
        }
    }

    private IQueryable<MaterializedMetadata> ApplyCriteria(
        IQueryable<MaterializedMetadata> query,
        MetadataCriteria criteria)
    {
        if (criteria.ContainerIds.Count > 0)
        {
            query = query.Where(metadata => criteria.ContainerIds.Contains(metadata.ContainerId));
        }

        query = query
            .ApplyCriteriaSet(criteria.Title, nameof(MaterializedMetadata.Title))
            .ApplyCriteriaSet(criteria.OriginalTitle, nameof(MaterializedMetadata.OriginalTitle))
            .ApplyCriteriaSet(criteria.Language, nameof(MaterializedMetadata.Language))
            .ApplyCriteriaSet(criteria.Overview, nameof(MaterializedMetadata.Overview))
            .ApplyCriteriaSet(criteria.ProductionStatus, nameof(MaterializedMetadata.ProductionStatus))
            .ApplyCriteriaSet(criteria.PublishDate, nameof(MaterializedMetadata.PublishDate))
            .ApplyCriteriaSet(criteria.BirthDate, nameof(MaterializedMetadata.BirthDate))
            .ApplyCriteriaSet(criteria.DeathDate, nameof(MaterializedMetadata.DeathDate))
            .ApplyCriteriaSet(criteria.Established, nameof(MaterializedMetadata.Established))
            .ApplyCriteriaSet(criteria.Dissolved, nameof(MaterializedMetadata.Dissolved))
            .ApplyCriteriaSet(criteria.Duration, nameof(MaterializedMetadata.Duration));

        query = ApplyAwardsCriteria(query, criteria.Awards);
        query = ApplyGenresCriteria(query, criteria.Genres);
        query = ApplyExternalIdsCriteria(query, criteria.ExternalIds);
        query = ApplyContentRatingsCriteria(query, criteria.ContentRatings);
        query = ApplyAlternateTextsCriteria(query, criteria.AlternateTexts);
        query = ApplyImagesCriteria(query, criteria.Images);
        query = ApplyPreviewsCriteria(query, criteria.Previews);
        query = ApplyReferencesCriteria(query, criteria.References);
        query = ApplyRelationshipsCriteria(query, criteria.Relationships);
        query = ApplyTagsCriteria(query, criteria.Tags);

        return query;
    }

    private IQueryable<MaterializedMetadata> ApplyCollectionGroups<TItem>(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<TItem> criteria,
        Func<TItem, IQueryable<Guid>> matchingContainerIds)
    {
        foreach (var group in criteria.Some)
        {
            if (group.Count == 0)
            {
                continue;
            }

            var includeIds = (
                from c in context.MetadataContributions.AsNoTracking()
                select c.MetadataContainerId).OrderBy(id => id).Take(0);

            foreach (var itemCriteria in group)
            {
                includeIds = includeIds.Union(matchingContainerIds(itemCriteria));
            }

            query = query.Where(m => includeIds.Contains(m.ContainerId));
        }

        if (criteria.None is { Count: > 0 })
        {
            var excludeIds = (
                from c in context.MetadataContributions.AsNoTracking()
                select c.MetadataContainerId).OrderBy(id => id).Take(0);

            foreach (var itemCriteria in criteria.None)
            {
                excludeIds = excludeIds.Union(matchingContainerIds(itemCriteria));
            }

            query = query.Where(m => !excludeIds.Contains(m.ContainerId));
        }

        return query;
    }

    private IQueryable<MaterializedMetadata> ApplyAwardsCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<AwardCriteria> criteria)
    {
        return ApplyCollectionGroups(
            query,
            criteria,
            itemCriteria =>
            {
                var awardsQuery = GetAwardsProjection()
                    .ApplyCriteriaSet(itemCriteria.Body, nameof(Award.Body))
                    .ApplyCriteriaSet(itemCriteria.Name, nameof(Award.Name))
                    .ApplyCriteriaSet(itemCriteria.Category, nameof(Award.Category))
                    .ApplyCriteriaSet(itemCriteria.Status, nameof(Award.Status))
                    .ApplyCriteriaSet(itemCriteria.Year, nameof(Award.Year))
                    .ApplyCriteriaSet(itemCriteria.Language, nameof(Award.Language))
                    .ApplyCriteriaSet(itemCriteria.Country, nameof(Award.Country));

                return
                    from c in context.MetadataContributions.AsNoTracking()
                    join a in awardsQuery on c.Id equals a.MetadataContributionId
                    select c.MetadataContainerId;
            });
    }

    private IQueryable<MaterializedMetadata> ApplyGenresCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<GenreCriteria> criteria)
    {
        return ApplyCollectionGroups(
            query,
            criteria,
            itemCriteria =>
            {
                var genresQuery = GetGenresProjection()
                    .ApplyCriteriaSet(itemCriteria.Name, nameof(Genre.Name));

                return
                    from c in context.MetadataContributions.AsNoTracking()
                    join g in genresQuery on c.Id equals g.MetadataContributionId
                    select c.MetadataContainerId;
            });
    }

    private IQueryable<MaterializedMetadata> ApplyExternalIdsCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<ExternalIdCriteria> criteria)
    {
        return ApplyCollectionGroups(
            query,
            criteria,
            itemCriteria =>
            {
                return MatchingContainerIdsByExternalId(itemCriteria);
            });
    }

    private IQueryable<ExternalId> DeduplicatedExternalIds()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from e in c.ExternalIds
            where c.Id == (
                from c2 in context.MetadataContributions
                from e2 in c2.ExternalIds
                where c2.MetadataContainerId == c.MetadataContainerId
                where e.Source != null
                    ? e2.Source != null && e2.Source.ToLower() == e.Source.ToLower()
                    : e2.Value.ToLower() == e.Value.ToLower()
                orderby c2.Ordinal
                select c2.Id).FirstOrDefault()
            orderby c.Ordinal, e.Ordinal
            select e;
    }

    private IQueryable<Guid> MatchingContainerIdsByExternalId(ExternalIdCriteria criterion)
    {
        var externalIds = DeduplicatedExternalIds()
            .ApplyCriteriaSet(criterion.Value, nameof(ExternalId.Value))
            .ApplyCriteriaSet(criterion.Source, nameof(ExternalId.Source));

        return (
            from c in context.MetadataContributions.AsNoTracking()
            join e in externalIds on c.Id equals e.MetadataContributionId
            select c.MetadataContainerId).Distinct();
    }

    private IQueryable<MaterializedMetadata> ApplyImagesCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<ImageCriteria> criteria)
    {
        return ApplyCollectionGroups(
            query,
            criteria,
            itemCriteria =>
            {
                var imagesQuery = GetImagesProjection()
                    .ApplyCriteriaSet(itemCriteria.Type, nameof(Image.Type))
                    .ApplyCriteriaSet(itemCriteria.MimeType, nameof(Image.MimeType))
                    .ApplyCriteriaSet(itemCriteria.Source, nameof(Image.Source))
                    .ApplyCriteriaSet(itemCriteria.Language, nameof(Image.Language))
                    .ApplyCriteriaSet(itemCriteria.Width, nameof(Image.Width))
                    .ApplyCriteriaSet(itemCriteria.Height, nameof(Image.Height));

                return
                    from c in context.MetadataContributions.AsNoTracking()
                    join i in imagesQuery on c.Id equals i.MetadataContributionId
                    select c.MetadataContainerId;
            });
    }

    private IQueryable<MaterializedMetadata> ApplyPreviewsCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<PreviewMetadataCriteria> criteria)
    {
        return ApplyCollectionGroups(
            query,
            criteria,
            itemCriteria =>
            {
                var previewsQuery = GetPreviewsProjection()
                    .ApplyCriteriaSet(itemCriteria.MimeType, nameof(Preview.MimeType))
                    .ApplyCriteriaSet(itemCriteria.Source, nameof(Preview.Source))
                    .ApplyCriteriaSet(itemCriteria.Title, nameof(Preview.Title))
                    .ApplyCriteriaSet(itemCriteria.ReleaseDate, nameof(Preview.ReleaseDate))
                    .ApplyCriteriaSet(itemCriteria.Language, nameof(Preview.Language));

                return
                    from c in context.MetadataContributions.AsNoTracking()
                    join p in previewsQuery on c.Id equals p.MetadataContributionId
                    select c.MetadataContainerId;
            });
    }

    private IQueryable<MaterializedMetadata> ApplyContentRatingsCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<ContentRatingCriteria> criteria)
    {
        return ApplyCollectionGroups(
            query,
            criteria,
            itemCriteria =>
            {
                var ratingsQuery = GetContentRatingsProjection()
                    .ApplyCriteriaSet(itemCriteria.Body, nameof(ContentRating.Body))
                    .ApplyCriteriaSet(itemCriteria.Rating, nameof(ContentRating.Rating));

                return
                    from c in context.MetadataContributions.AsNoTracking()
                    join r in ratingsQuery on c.Id equals r.MetadataContributionId
                    select c.MetadataContainerId;
            });
    }

    private IQueryable<MaterializedMetadata> ApplyAlternateTextsCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<AlternateTextCriteria> criteria)
    {
        return ApplyCollectionGroups(
            query,
            criteria,
            itemCriteria =>
            {
                var textsQuery = GetAlternateTextsProjection()
                    .ApplyCriteriaSet(itemCriteria.Kind, nameof(AlternateText.Kind))
                    .ApplyCriteriaSet(itemCriteria.Language, nameof(AlternateText.Language))
                    .ApplyCriteriaSet(itemCriteria.Text, nameof(AlternateText.Text));

                return
                    from c in context.MetadataContributions.AsNoTracking()
                    join a in textsQuery on c.Id equals a.MetadataContributionId
                    select c.MetadataContainerId;
            });
    }

    private IQueryable<MaterializedMetadata> ApplyReferencesCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<ReferenceMetadataCriteria> criteria)
    {
        return ApplyCollectionGroups(
            query,
            criteria,
            itemCriteria =>
            {
                var refsQuery = GetReferencesProjection()
                    .ApplyCriteriaSet(itemCriteria.Kind, nameof(Reference.Kind))
                    .ApplyCriteriaSet(itemCriteria.DisplayName, nameof(Reference.DisplayName))
                    .ApplyCriteriaSet(itemCriteria.Language, nameof(Reference.Language));

                return
                    from c in context.MetadataContributions.AsNoTracking()
                    join r in refsQuery on c.Id equals r.MetadataContributionId
                    select c.MetadataContainerId;
            });
    }

    private IQueryable<MaterializedMetadata> ApplyRelationshipsCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<RelationshipCriteria> criteria)
    {
        return ApplyCollectionGroups(
            query,
            criteria,
            itemCriteria =>
            {
                var relationshipsQuery = ApplyRelationshipItemCriteria(GetRelationshipsProjection(), itemCriteria);

                return
                    from c in context.MetadataContributions.AsNoTracking()
                    join r in relationshipsQuery on c.Id equals r.MetadataContributionId
                    select c.MetadataContainerId;
            });
    }

    private IQueryable<Relationship> ApplyRelationshipItemCriteria(
        IQueryable<Relationship> relationships,
        RelationshipCriteria criterion)
    {
        foreach (var kind in criterion.Kind.And)
        {
            relationships = ApplyStringRelationshipPredicate(relationships, kind, r => r.Kind);
        }

        foreach (var qualifier in criterion.Qualifier.And)
        {
            relationships = ApplyStringRelationshipPredicate(relationships, qualifier, r => r.Qualifier);
        }

        foreach (var type in criterion.Type.And)
        {
            if (type.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
            {
                var typedContainerIds = AllTypedContainerIds();
                relationships = type.Operator == CriteriaValueOperator.IsNotNull
                    ? relationships.Where(r =>
                        r.RelatedMetadataContainerId != null &&
                        typedContainerIds.Contains(r.RelatedMetadataContainerId.Value))
                    : relationships.Where(r =>
                        r.RelatedMetadataContainerId == null ||
                        !typedContainerIds.Contains(r.RelatedMetadataContainerId.Value));
                continue;
            }

            var typeContainerIds = MatchingContainerIdsByType(type);
            relationships = relationships.Where(r =>
                r.RelatedMetadataContainerId != null &&
                typeContainerIds.Contains(r.RelatedMetadataContainerId.Value));
        }

        foreach (var subject in criterion.Subject.And)
        {
            var subjectContainerIds = MatchingSubjectContainerIds(subject);
            relationships = IsNegativeSubjectOperator(subject.Operator)
                ? relationships.Where(r =>
                    r.RelatedMetadataContainerId == null ||
                    !subjectContainerIds.Contains(r.RelatedMetadataContainerId.Value))
                : relationships.Where(r =>
                    r.RelatedMetadataContainerId != null &&
                    subjectContainerIds.Contains(r.RelatedMetadataContainerId.Value));
        }

        foreach (var externalId in criterion.ExternalIds.SomeFlat)
        {
            var subjectContainerIds = MatchingContainerIdsByExternalId(externalId);
            relationships = relationships.Where(r =>
                r.RelatedMetadataContainerId != null &&
                subjectContainerIds.Contains(r.RelatedMetadataContainerId.Value));
        }

        foreach (var externalId in criterion.ExternalIds.None)
        {
            var subjectContainerIds = MatchingContainerIdsByExternalId(externalId);
            relationships = relationships.Where(r =>
                r.RelatedMetadataContainerId == null ||
                !subjectContainerIds.Contains(r.RelatedMetadataContainerId.Value));
        }

        return relationships;
    }

    private IQueryable<Guid> MatchingContainerIdsByType(PropertyCriterion<string> criterion)
    {
        var subjectIds = context.Subjects.AsNoTracking()
            .Where(ApplyTypePredicate<Subject>(criterion, s => s.Kind))
            .Select(s => s.MetadataContainerId);

        var levelIds = context.ContentHierarchyItems.AsNoTracking()
            .Where(ApplyTypePredicate<ContentHierarchyItem>(criterion, i => i.Level))
            .Select(i => i.MetadataContainerId);

        var contentTypeIds = context.ContentHierarchyItems.AsNoTracking()
            .Where(ApplyTypePredicate<ContentHierarchyItem>(criterion, i => i.ContentType))
            .Select(i => i.MetadataContainerId);

        return subjectIds.Concat(levelIds).Concat(contentTypeIds).Distinct();
    }

    private IQueryable<Guid> AllTypedContainerIds()
    {
        var subjectIds = context.Subjects.AsNoTracking().Select(s => s.MetadataContainerId);
        var contentIds = context.ContentHierarchyItems.AsNoTracking().Select(i => i.MetadataContainerId);
        return subjectIds.Concat(contentIds).Distinct();
    }

    private IQueryable<Guid> MatchingSubjectContainerIds(PropertyCriterion<string> criterion)
    {
        var positive = ToInnerPositive(criterion);

        var byTitle = ApplyStringPredicate(context.MetadataContributions.AsNoTracking(), positive, c => c.Title)
            .Select(c => c.MetadataContainerId);

        var byAlias = ApplyStringPredicate(
            context.AlternateTexts.AsNoTracking().Where(a => a.Kind.ToLower() == "name"),
            positive,
            a => a.Text)
            .Join(
                context.MetadataContributions.AsNoTracking(),
                a => a.MetadataContributionId,
                c => c.Id,
                (_, c) => c.MetadataContainerId);

        return byTitle.Concat(byAlias).Distinct();
    }

    private IQueryable<MaterializedMetadata> ApplyTagsCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<TagCriteria> criteria)
    {
        return ApplyCollectionGroups(
            query,
            criteria,
            itemCriteria =>
                from t in context.Tags.AsNoTracking().ApplyCriteriaSet(itemCriteria.Name, nameof(Tag.Name))
                from mc in t.MetadataContainers
                select mc.Id);
    }

    private IQueryable<MaterializedMetadata> GetMetadataProjection(string? preferredLanguage = null)
    {
        return
            from mc in context.MetadataContainers.AsNoTracking().AsSplitQuery()
            select new MaterializedMetadata
            {
                ContainerId = mc.Id,
                ContentHierarchyItemId = context.ContentHierarchyItems
                    .Where(item => item.MetadataContainerId == mc.Id)
                    .Select(item => (Guid?)item.Id)
                    .FirstOrDefault(),
                SubjectId = context.Subjects
                    .Where(subject => subject.MetadataContainerId == mc.Id)
                    .Select(subject => (Guid?)subject.Id)
                    .FirstOrDefault(),
                Title = (
                    from c in mc.Contributions
                    where c.Title != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1),
                        c.Ordinal
                    select c.Title).FirstOrDefault(),
                OriginalTitle = (
                    from c in mc.Contributions
                    where c.OriginalTitle != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1),
                        c.Ordinal
                    select c.OriginalTitle).FirstOrDefault(),
                SortTitle = (
                    from c in mc.Contributions
                    where c.SortTitle != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1),
                        c.Ordinal
                    select c.SortTitle).FirstOrDefault(),
                Language = (
                    from c in mc.Contributions
                    where c.Language != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1),
                        c.Ordinal
                    select c.Language).FirstOrDefault(),
                Overview = (
                    from c in mc.Contributions
                    where c.Overview != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1),
                        c.Ordinal
                    select c.Overview).FirstOrDefault(),
                PublishDate = (
                    from c in mc.Contributions
                    where c.PublishDate != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1),
                        c.Ordinal
                    select c.PublishDate).FirstOrDefault(),
                Duration = (
                    from c in mc.Contributions
                    where c.Duration != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1),
                        c.Ordinal
                    select c.Duration).FirstOrDefault(),
                SequenceNumber = (
                    from c in mc.Contributions
                    where c.SequenceNumber != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1),
                        c.Ordinal
                    select c.SequenceNumber).FirstOrDefault(),
                ProductionStatus = (
                    from c in mc.Contributions
                    where c.ProductionStatus != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1),
                        c.Ordinal
                    select c.ProductionStatus).FirstOrDefault(),
                Established = (
                    from c in mc.Contributions
                    where c.Established != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1),
                        c.Ordinal
                    select c.Established).FirstOrDefault(),
                Dissolved = (
                    from c in mc.Contributions
                    where c.Dissolved != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1),
                        c.Ordinal
                    select c.Dissolved).FirstOrDefault(),
                BirthDate = (
                    from c in mc.Contributions
                    where c.BirthDate != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1),
                        c.Ordinal
                    select c.BirthDate).FirstOrDefault(),
                DeathDate = (
                    from c in mc.Contributions
                    where c.DeathDate != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1),
                        c.Ordinal
                    select c.DeathDate).FirstOrDefault(),
                Genres = (
                    from c in context.MetadataContributions.AsNoTracking()
                    join value in GetGenresProjection() on c.Id equals value.MetadataContributionId
                    where c.MetadataContainerId == mc.Id
                    select value).ToList(),
                ExternalIds = (
                    from c in context.MetadataContributions.AsNoTracking()
                    join value in DeduplicatedExternalIds() on c.Id equals value.MetadataContributionId
                    where c.MetadataContainerId == mc.Id
                    select value).ToList(),
                ContentRatings = (
                    from c in context.MetadataContributions.AsNoTracking()
                    join value in GetContentRatingsProjection() on c.Id equals value.MetadataContributionId
                    where c.MetadataContainerId == mc.Id
                    select value).ToList(),
                Awards = (
                    from c in context.MetadataContributions.AsNoTracking()
                    join value in GetAwardsProjection() on c.Id equals value.MetadataContributionId
                    where c.MetadataContainerId == mc.Id
                    select value).ToList(),
                AlternateTexts = (
                    from c in context.MetadataContributions.AsNoTracking()
                    join value in GetAlternateTextsProjection() on c.Id equals value.MetadataContributionId
                    where c.MetadataContainerId == mc.Id
                    select value).ToList(),
                References = (
                    from c in context.MetadataContributions.AsNoTracking()
                    join value in GetReferencesProjection() on c.Id equals value.MetadataContributionId
                    where c.MetadataContainerId == mc.Id
                    select value).ToList(),
                Relationships = (
                    from c in context.MetadataContributions.AsNoTracking()
                    join value in GetRelationshipsProjection() on c.Id equals value.MetadataContributionId
                    where c.MetadataContainerId == mc.Id
                    select value).ToList(),
                Images = (
                    from c in context.MetadataContributions.AsNoTracking()
                    join value in GetImagesProjection() on c.Id equals value.MetadataContributionId
                    where c.MetadataContainerId == mc.Id
                    select value).ToList(),
                Previews = (
                    from c in context.MetadataContributions.AsNoTracking()
                    join value in GetPreviewsProjection() on c.Id equals value.MetadataContributionId
                    where c.MetadataContainerId == mc.Id
                    select value).ToList(),
                CustomMetadata = (
                    from c in context.MetadataContributions.AsNoTracking()
                    from value in c.CustomMetadata
                    where c.MetadataContainerId == mc.Id
                    where c.Id == (
                        from c2 in context.MetadataContributions
                        from value2 in c2.CustomMetadata
                        where c2.MetadataContainerId == c.MetadataContainerId
                        where value2.Key.ToLower() == value.Key.ToLower()
                        orderby c2.Ordinal
                        select c2.Id).FirstOrDefault()
                    orderby c.Ordinal
                    select value).ToList(),
            };
    }

    private IQueryable<AlternateText> GetAlternateTextsProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from a in c.AlternateTexts
            where c.Id == (
                from c2 in context.MetadataContributions
                from a2 in c2.AlternateTexts
                where c2.MetadataContainerId == c.MetadataContainerId
                where a2.Kind.ToLower() == a.Kind.ToLower()
                where
                    (a2.Language == null && a.Language == null) ||
                    (a2.Language != null && a.Language != null && a2.Language.ToLower() == a.Language.ToLower())
                where a2.Text.ToLower() == a.Text.ToLower()
                orderby c2.Ordinal
                select c2.Id).FirstOrDefault()
            orderby c.Ordinal, a.Ordinal
            select a;
    }

    private IQueryable<Award> GetAwardsProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from a in c.Awards
            where c.Id == (
                from c2 in context.MetadataContributions
                from a2 in c2.Awards
                where c2.MetadataContainerId == c.MetadataContainerId
                where a2.Body.ToLower() == a.Body.ToLower()
                where a2.Name.ToLower() == a.Name.ToLower()
                where a2.Year == a.Year
                where
                    (a2.Category == null && a.Category == null) ||
                    (a2.Category != null && a.Category != null && a2.Category.ToLower() == a.Category.ToLower())
                orderby c2.Ordinal
                select c2.Id).FirstOrDefault()
            orderby c.Ordinal, a.Ordinal
            select a;
    }

    private IQueryable<ContentRating> GetContentRatingsProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from r in c.ContentRatings
            where c.Id == (
                from c2 in context.MetadataContributions
                from r2 in c2.ContentRatings
                where c2.MetadataContainerId == c.MetadataContainerId
                where r2.Body.ToLower() == r.Body.ToLower()
                orderby c2.Ordinal
                select c2.Id).FirstOrDefault()
            orderby c.Ordinal, r.Ordinal
            select r;
    }

    private IQueryable<Genre> GetGenresProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from g in c.Genres
            where c.Id == (
                from c2 in context.MetadataContributions
                from g2 in c2.Genres
                where c2.MetadataContainerId == c.MetadataContainerId
                where g2.Name.ToLower() == g.Name.ToLower()
                orderby c2.Ordinal
                select c2.Id).FirstOrDefault()
            orderby c.Ordinal, g.Ordinal
            select g;
    }

    private IQueryable<Image> GetImagesProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from i in c.Images
            where c.Id == (
                from c2 in context.MetadataContributions
                from i2 in c2.Images
                where c2.MetadataContainerId == c.MetadataContainerId
                where i2.Type.ToLower() == i.Type.ToLower()
                orderby c2.Ordinal
                select c2.Id).FirstOrDefault()
            orderby c.Ordinal, i.Ordinal
            select i;
    }

    private IQueryable<Preview> GetPreviewsProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from p in c.Previews
            where c.Id == (
                from c2 in context.MetadataContributions
                from p2 in c2.Previews
                where c2.MetadataContainerId == c.MetadataContainerId
                where p2.Kind.ToLower() == p.Kind.ToLower()
                orderby c2.Ordinal
                select c2.Id).FirstOrDefault()
            orderby c.Ordinal, p.Ordinal
            select p;
    }

    private IQueryable<Reference> GetReferencesProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from r in c.References
            where c.Id == (
                from c2 in context.MetadataContributions
                from r2 in c2.References
                where c2.MetadataContainerId == c.MetadataContainerId
                where r2.Source.ToLower() == r.Source.ToLower()
                where r2.Kind.ToLower() == r.Kind.ToLower()
                where
                    (r2.Language == null && r.Language == null) ||
                    (r2.Language != null && r.Language != null && r2.Language.ToLower() == r.Language.ToLower())
                orderby c2.Ordinal
                select c2.Id).FirstOrDefault()
            orderby c.Ordinal, r.Ordinal
            select r;
    }

    private IQueryable<Relationship> GetRelationshipsProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from r in c.Relationships
            where r.Qualifier != null || !(
                from c3 in context.MetadataContributions
                from r3 in c3.Relationships
                where c3.MetadataContainerId == c.MetadataContainerId
                where r3.Kind.ToLower() == r.Kind.ToLower()
                where r3.RelatedMetadataContainerId == r.RelatedMetadataContainerId
                where r3.Qualifier != null
                select r3).Any()
            where c.Id == (
                from c2 in context.MetadataContributions
                from r2 in c2.Relationships
                where c2.MetadataContainerId == c.MetadataContainerId
                where r2.Kind.ToLower() == r.Kind.ToLower()
                where r2.RelatedMetadataContainerId == r.RelatedMetadataContainerId
                where
                    (r2.Qualifier == null && r.Qualifier == null) ||
                    (r2.Qualifier != null && r.Qualifier != null && r2.Qualifier.ToLower() == r.Qualifier.ToLower())
                orderby c2.Ordinal
                select c2.Id).FirstOrDefault()
            orderby c.Ordinal, r.Ordinal
            select r;
    }
}
