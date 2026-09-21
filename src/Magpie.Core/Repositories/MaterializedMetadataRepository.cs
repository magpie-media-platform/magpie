using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Paging;

using Microsoft.EntityFrameworkCore;
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
            ? query.OrderBy(m => EF.Property<object>(m, sortProperty)).ThenBy(m => m.ContainerId)
            : query.OrderByDescending(m => EF.Property<object>(m, sortProperty))
                .ThenBy(m => m.ContainerId);
    }

    public async Task<IEnumerable<MaterializedMetadata>> ListAllAsync(CancellationToken cancellationToken)
    {
        var metadata = await GetMetadataProjection()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        await HydrateCollectionsAsync(metadata, cancellationToken).ConfigureAwait(false);
        return metadata;
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
            await HydrateCollectionsAsync(materialized, cancellationToken).ConfigureAwait(false);

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

            IQueryable<Guid>? includeIds = null;
            foreach (var itemCriteria in group)
            {
                var branch = matchingContainerIds(itemCriteria);
                includeIds = includeIds is null ? branch : includeIds.Union(branch);
            }

            query = query.Where(m => includeIds!.Contains(m.ContainerId));
        }

        if (criteria.None is { Count: > 0 })
        {
            IQueryable<Guid>? excludeIds = null;
            foreach (var itemCriteria in criteria.None)
            {
                var branch = matchingContainerIds(itemCriteria);
                excludeIds = excludeIds is null ? branch : excludeIds.Union(branch);
            }

            query = query.Where(m => !excludeIds!.Contains(m.ContainerId));
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

    private IQueryable<ExternalId> GetExternalIdsProjection()
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
                    : e2.Source == null && e2.Value.ToLower() == e.Value.ToLower()
                orderby c2.Ordinal, c2.Id
                select c2.Id).FirstOrDefault()
            orderby c.Ordinal, e.Ordinal
            select e;
    }

    private IQueryable<Guid> MatchingContainerIdsByExternalId(ExternalIdCriteria criterion)
    {
        var externalIds = GetExternalIdsProjection()
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
            relationships = relationships.ApplyStringPredicate(kind, r => r.Kind);
        }

        foreach (var qualifier in criterion.Qualifier.And)
        {
            relationships = relationships.ApplyStringPredicate(qualifier, r => r.Qualifier);
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
            var negated = subject.Operator is CriteriaValueOperator.NotContainsString
                or CriteriaValueOperator.NotEquals;
            relationships = negated
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

    private IQueryable<Guid> AllTypedContainerIds()
    {
        var subjectIds = context.Subjects.AsNoTracking().Select(s => s.MetadataContainerId);
        var contentIds = context.ContentHierarchyItems.AsNoTracking().Select(i => i.MetadataContainerId);
        return subjectIds.Concat(contentIds).Distinct();
    }

    private IQueryable<Guid> MatchingContainerIdsByType(PropertyCriterion<string> criterion)
    {
        var subjectIds = context.Subjects.AsNoTracking()
            .ApplyStringPredicate(criterion, s => s.Kind)
            .Select(s => s.MetadataContainerId);

        var levelIds = context.ContentHierarchyItems.AsNoTracking()
            .ApplyStringPredicate(criterion, i => i.Level)
            .Select(i => i.MetadataContainerId);

        var contentTypeIds = context.ContentHierarchyItems.AsNoTracking()
            .ApplyStringPredicate(criterion, i => i.ContentType)
            .Select(i => i.MetadataContainerId);

        return subjectIds.Concat(levelIds).Concat(contentTypeIds).Distinct();
    }

    private IQueryable<Guid> MatchingSubjectContainerIds(PropertyCriterion<string> criterion)
    {
        var positive = criterion.ToInnerPositive();

        var byTitle = context.MetadataContributions.AsNoTracking()
            .ApplyStringPredicate(positive, c => c.Title)
            .Select(c => c.MetadataContainerId);

        var byAlias = context.AlternateTexts.AsNoTracking().Where(a => a.Kind.ToLower() == "name")
            .ApplyStringPredicate(positive, a => a.Text)
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
            };
    }

    private async Task HydrateCollectionsAsync(
        IList<MaterializedMetadata> metadata,
        CancellationToken cancellationToken)
    {
        if (metadata.Count == 0)
        {
            return;
        }

        var containerIds = metadata.Select(value => value.ContainerId).ToList();
        var contributions = await context.MetadataContributions.AsNoTracking()
            .Where(value => containerIds.Contains(value.MetadataContainerId))
            .Include(value => value.AlternateTexts)
            .Include(value => value.Awards)
            .Include(value => value.ContentRatings)
            .Include(value => value.CustomMetadata)
            .Include(value => value.ExternalIds)
            .Include(value => value.Genres)
            .Include(value => value.Images)
            .Include(value => value.Previews)
            .Include(value => value.References)
            .Include(value => value.Relationships)
            .AsSplitQuery()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byContainer = contributions
            .GroupBy(value => value.MetadataContainerId)
            .ToDictionary(group => group.Key, group => group.OrderBy(value => value.Ordinal)
                .ThenBy(value => value.Id.ToString(), StringComparer.Ordinal).ToList());

        for (var index = 0; index < metadata.Count; index++)
        {
            byContainer.TryGetValue(metadata[index].ContainerId, out var values);
            values ??= [];
            var relationships = values.SelectMany(value => value.Relationships)
                .Where(value => value.Qualifier != null || !values.SelectMany(item => item.Relationships).Any(
                    qualified => qualified.Qualifier != null &&
                        EqualKey(qualified.Kind, value.Kind) &&
                        qualified.RelatedMetadataContainerId == value.RelatedMetadataContainerId));

            metadata[index] = metadata[index] with
            {
                AlternateTexts = Merge(values, value => value.AlternateTexts,
                    value => (Key(value.Kind), Key(value.Language), Key(value.Text)), value => value.Ordinal),
                Awards = Merge(values, value => value.Awards,
                    value => (Key(value.Body), Key(value.Name), value.Year, Key(value.Category)),
                    value => value.Ordinal),
                ContentRatings = Merge(
                    values, value => value.ContentRatings, value => Key(value.Body), value => value.Ordinal),
                CustomMetadata = Merge(values, value => value.CustomMetadata, value => Key(value.Key)),
                ExternalIds = Merge(values, value => value.ExternalIds,
                    value => value.Source == null
                        ? ((string?)null, Key(value.Value))
                        : (Key(value.Source), (string?)null), value => value.Ordinal),
                Genres = Merge(values, value => value.Genres, value => Key(value.Name), value => value.Ordinal),
                Images = Merge(values, value => value.Images, value => Key(value.Type), value => value.Ordinal),
                Previews = Merge(values, value => value.Previews, value => Key(value.Kind), value => value.Ordinal),
                References = Merge(values, value => value.References,
                    value => (Key(value.Source), Key(value.Kind), Key(value.Language)), value => value.Ordinal),
                Relationships = Merge(values, contribution => relationships
                        .Where(value => value.MetadataContributionId == contribution.Id),
                    value => (Key(value.Kind), value.RelatedMetadataContainerId, Key(value.Qualifier)),
                    value => value.Ordinal)
            };
        }
    }

    private List<T> Merge<T, TKey>(
        IEnumerable<MetadataContribution> contributions,
        Func<MetadataContribution, IEnumerable<T>> items,
        Func<T, TKey> key,
        Func<T, int>? order = null)
    {
        var seen = new HashSet<TKey>();
        var result = new List<T>();
        foreach (var contribution in contributions)
        {
            var contributionItems = items(contribution);
            if (order is not null)
            {
                contributionItems = contributionItems.OrderBy(order);
            }

            var ordered = contributionItems.ToList();
            result.AddRange(ordered.Where(value => !seen.Contains(key(value))));
            seen.UnionWith(ordered.Select(key));
        }

        return result;
    }

    private string? Key(string? value) => value?.ToLower();

    private bool EqualKey(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private IQueryable<AlternateText> GetAlternateTextsProjection()
    {
        return
            from contribution in context.MetadataContributions.AsNoTracking()
            from value in contribution.AlternateTexts
            where !context.MetadataContributions.Any(earlier =>
                earlier.MetadataContainerId == contribution.MetadataContainerId &&
                (earlier.Ordinal < contribution.Ordinal ||
                    earlier.Ordinal == contribution.Ordinal &&
                    string.Compare(earlier.Id.ToString(), contribution.Id.ToString()) < 0) &&
                earlier.AlternateTexts.Any(other => other.Kind.ToLower() == value.Kind.ToLower() &&
                    other.Language!.ToLower() == value.Language!.ToLower() &&
                    other.Text.ToLower() == value.Text.ToLower()))
            select value;
    }

    private IQueryable<Award> GetAwardsProjection()
    {
        return
            from contribution in context.MetadataContributions.AsNoTracking()
            from value in contribution.Awards
            where !context.MetadataContributions.Any(earlier =>
                earlier.MetadataContainerId == contribution.MetadataContainerId &&
                (earlier.Ordinal < contribution.Ordinal ||
                    earlier.Ordinal == contribution.Ordinal &&
                    string.Compare(earlier.Id.ToString(), contribution.Id.ToString()) < 0) &&
                earlier.Awards.Any(other =>
                    other.Body.ToLower() == value.Body.ToLower() && other.Name.ToLower() == value.Name.ToLower() &&
                    other.Year == value.Year && other.Category!.ToLower() == value.Category!.ToLower()))
            select value;
    }

    private IQueryable<ContentRating> GetContentRatingsProjection()
    {
        return
            from contribution in context.MetadataContributions.AsNoTracking()
            from value in contribution.ContentRatings
            where !context.MetadataContributions.Any(earlier =>
                earlier.MetadataContainerId == contribution.MetadataContainerId &&
                (earlier.Ordinal < contribution.Ordinal ||
                    earlier.Ordinal == contribution.Ordinal &&
                    string.Compare(earlier.Id.ToString(), contribution.Id.ToString()) < 0) &&
                earlier.ContentRatings.Any(other => other.Body.ToLower() == value.Body.ToLower()))
            select value;
    }

    private IQueryable<Genre> GetGenresProjection()
    {
        return
            from contribution in context.MetadataContributions.AsNoTracking()
            from value in contribution.Genres
            where !context.MetadataContributions.Any(earlier =>
                earlier.MetadataContainerId == contribution.MetadataContainerId &&
                (earlier.Ordinal < contribution.Ordinal ||
                    earlier.Ordinal == contribution.Ordinal &&
                    string.Compare(earlier.Id.ToString(), contribution.Id.ToString()) < 0) &&
                earlier.Genres.Any(other => other.Name.ToLower() == value.Name.ToLower()))
            select value;
    }

    private IQueryable<Image> GetImagesProjection()
    {
        return
            from contribution in context.MetadataContributions.AsNoTracking()
            from value in contribution.Images
            where !context.MetadataContributions.Any(earlier =>
                earlier.MetadataContainerId == contribution.MetadataContainerId &&
                (earlier.Ordinal < contribution.Ordinal ||
                    earlier.Ordinal == contribution.Ordinal &&
                    string.Compare(earlier.Id.ToString(), contribution.Id.ToString()) < 0) &&
                earlier.Images.Any(other => other.Type.ToLower() == value.Type.ToLower()))
            select value;
    }

    private IQueryable<Preview> GetPreviewsProjection()
    {
        return
            from contribution in context.MetadataContributions.AsNoTracking()
            from value in contribution.Previews
            where !context.MetadataContributions.Any(earlier =>
                earlier.MetadataContainerId == contribution.MetadataContainerId &&
                (earlier.Ordinal < contribution.Ordinal ||
                    earlier.Ordinal == contribution.Ordinal &&
                    string.Compare(earlier.Id.ToString(), contribution.Id.ToString()) < 0) &&
                earlier.Previews.Any(other => other.Kind.ToLower() == value.Kind.ToLower()))
            select value;
    }

    private IQueryable<Reference> GetReferencesProjection()
    {
        return
            from contribution in context.MetadataContributions.AsNoTracking()
            from value in contribution.References
            where !context.MetadataContributions.Any(earlier =>
                earlier.MetadataContainerId == contribution.MetadataContainerId &&
                (earlier.Ordinal < contribution.Ordinal ||
                    earlier.Ordinal == contribution.Ordinal &&
                    string.Compare(earlier.Id.ToString(), contribution.Id.ToString()) < 0) &&
                earlier.References.Any(other =>
                    other.Source.ToLower() == value.Source.ToLower() && other.Kind.ToLower() == value.Kind.ToLower() &&
                    other.Language!.ToLower() == value.Language!.ToLower()))
            select value;
    }

    private IQueryable<Relationship> GetRelationshipsProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from r in c.Relationships
            where r.Qualifier != null || !(
                from c2 in context.MetadataContributions
                from r2 in c2.Relationships
                where c2.MetadataContainerId == c.MetadataContainerId
                where r2.Qualifier != null
                where r2.Kind.ToLower() == r.Kind.ToLower()
                where r2.RelatedMetadataContainerId == r.RelatedMetadataContainerId
                select r2).Any()
            select r;
    }
}
