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
using Magpie.Models.Sorting;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class MaterializedMetadataRepository(
    MagpieDbContext context,
    ILogger<MaterializedMetadataRepository> logger) : IMaterializedMetadataRepository
{
    record Ranked<T>(T Item, Guid ContainerId, int Ordinal);

    public Task<IEnumerable<MaterializedMetadata>> ListAllAsync(CancellationToken cancellationToken)
    {
        return ListAllAsync(null, cancellationToken);
    }

    public Task<IEnumerable<MaterializedMetadata>> ListAllAsync(
        string? preferredLanguage,
        CancellationToken cancellationToken)
    {
        var query = GetMetadataProjection(preferredLanguage);

        return MaterializeAsync(query, cancellationToken);
    }

    public Task<PagedList<MaterializedMetadata>> QueryAsync(
        MetadataCriteria query,
        PagingParameter? paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        return QueryAsync(query, paging, sort, null, cancellationToken);
    }

    public async Task<PagedList<MaterializedMetadata>> QueryAsync(
        MetadataCriteria query,
        PagingParameter? paging,
        SortParameter? sort,
        string? preferredLanguage,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projection = ApplyCriteria(GetMetadataProjection(preferredLanguage), query);
            var totalItems = await projection.CountAsync(cancellationToken).ConfigureAwait(false);

            projection = ApplySort(projection, sort);

            if (paging is not null)
            {
                projection = projection
                    .Skip((paging.PageNumber - 1) * paging.PageSize)
                    .Take(paging.PageSize);
            }

            var materialized = await MaterializeAsync(projection, cancellationToken).ConfigureAwait(false);

            return new(materialized, paging?.PageNumber, paging?.PageSize, totalItems);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error querying materialized metadata");
            throw;
        }
    }

    private async Task<IEnumerable<MaterializedMetadata>> MaterializeAsync(
        IQueryable<MaterializedMetadata> query,
        CancellationToken cancellationToken)
    {
        try
        {
            var scalars = await query
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var genresByContainer = (
                from r in await GetGenresProjection().ToListAsync(cancellationToken).ConfigureAwait(false)
                group r by r.ContainerId into g
                select new { g.Key, Items = g.Select(i => i.Item) })
                .ToDictionary(k => k.Key, v => v.Items.ToList());

            var externalIdsByContainer = (
                from r in await GetExternalIdsProjection().ToListAsync(cancellationToken).ConfigureAwait(false)
                group r by r.ContainerId into g
                select new { g.Key, Items = g.Select(i => i.Item) })
                .ToDictionary(k => k.Key, v => v.Items.ToList());

            var contentRatingsByContainer = (
                from r in await GetContentRatingsProjection().ToListAsync(cancellationToken).ConfigureAwait(false)
                group r by r.ContainerId into g
                select new { g.Key, Items = g.Select(i => i.Item) })
                .ToDictionary(k => k.Key, v => v.Items.ToList());

            var awardsByContainer = (
                from r in await GetAwardsProjection().ToListAsync(cancellationToken).ConfigureAwait(false)
                group r by r.ContainerId into g
                select new { g.Key, Items = g.Select(i => i.Item) })
                .ToDictionary(k => k.Key, v => v.Items.ToList());

            var alternateTextsByContainer = (
                from r in await GetAlternateTextsProjection().ToListAsync(cancellationToken).ConfigureAwait(false)
                group r by r.ContainerId into g
                select new { g.Key, Items = g.Select(i => i.Item) })
                .ToDictionary(k => k.Key, v => v.Items.ToList());

            var referencesByContainer = (
                from r in await GetReferencesProjection().ToListAsync(cancellationToken).ConfigureAwait(false)
                group r by r.ContainerId into g
                select new { g.Key, Items = g.Select(i => i.Item) })
                .ToDictionary(k => k.Key, v => v.Items.ToList());

            var relationshipsByContainer = (
                from r in await GetRelationshipsProjection().ToListAsync(cancellationToken).ConfigureAwait(false)
                group r by r.ContainerId into g
                select new { g.Key, Items = g.Select(i => i.Item) })
                .ToDictionary(k => k.Key, v => v.Items.ToList());

            var imagesByContainer = (
                from r in await GetImagesProjection().ToListAsync(cancellationToken).ConfigureAwait(false)
                group r by r.ContainerId into g
                select new { g.Key, Items = g.Select(i => i.Item) })
                .ToDictionary(k => k.Key, v => v.Items.ToList());

            var previewsByContainer = (
                from r in await GetPreviewsProjection().ToListAsync(cancellationToken).ConfigureAwait(false)
                group r by r.ContainerId into g
                select new { g.Key, Items = g.Select(i => i.Item) })
                .ToDictionary(k => k.Key, v => v.Items.ToList());

            var customMetadataByContainer = (
                from r in await GetCustomMetadataProjection().ToListAsync(cancellationToken).ConfigureAwait(false)
                group r by r.ContainerId into g
                select new { g.Key, Items = g.Select(i => i.Item) })
                .ToDictionary(k => k.Key, v => v.Items.ToList());

            return [.. (
                from s in scalars
                select s with
                {
                    Genres = genresByContainer.GetValueOrDefault(s.ContainerId),
                    ExternalIds = externalIdsByContainer.GetValueOrDefault(s.ContainerId),
                    ContentRatings = contentRatingsByContainer.GetValueOrDefault(s.ContainerId),
                    Awards = awardsByContainer.GetValueOrDefault(s.ContainerId),
                    AlternateTexts = alternateTextsByContainer.GetValueOrDefault(s.ContainerId),
                    References = referencesByContainer.GetValueOrDefault(s.ContainerId),
                    Relationships = relationshipsByContainer.GetValueOrDefault(s.ContainerId),
                    Images = imagesByContainer.GetValueOrDefault(s.ContainerId),
                    Previews = previewsByContainer.GetValueOrDefault(s.ContainerId),
                    CustomMetadata = customMetadataByContainer.GetValueOrDefault(s.ContainerId),
                })];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing all materialized metadata");
            throw;
        }
    }

    private IQueryable<MaterializedMetadata> ApplyCriteria(
         IQueryable<MaterializedMetadata> query,
         MetadataCriteria criteria)
    {
        query = query
            .ApplyAllStringCriteria(criteria.Title, nameof(MaterializedMetadata.Title))
            .ApplyAllStringCriteria(criteria.OriginalTitle, nameof(MaterializedMetadata.OriginalTitle))
            .ApplyAllStringCriteria(criteria.Language, nameof(MaterializedMetadata.Language))
            .ApplyAllStringCriteria(criteria.Overview, nameof(MaterializedMetadata.Overview))
            .ApplyAllStringCriteria(criteria.ProductionStatus, nameof(MaterializedMetadata.ProductionStatus))
            .ApplyAllComparableCriteria(criteria.PublishDate, nameof(MaterializedMetadata.PublishDate))
            .ApplyAllComparableCriteria(criteria.BirthDate, nameof(MaterializedMetadata.BirthDate))
            .ApplyAllComparableCriteria(criteria.DeathDate, nameof(MaterializedMetadata.DeathDate))
            .ApplyAllComparableCriteria(criteria.Established, nameof(MaterializedMetadata.Established))
            .ApplyAllComparableCriteria(criteria.Dissolved, nameof(MaterializedMetadata.Dissolved))
            .ApplyAllComparableCriteria(criteria.Duration, nameof(MaterializedMetadata.Duration));

        query = ApplyAwardsCriteria(query, criteria.Awards);

        return query;
    }

    private IQueryable<MaterializedMetadata> ApplyAwardsCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<AllCriteria<AwardCriteria>> criteria)
    {
        if (criteria.Items is not { All.Count: > 0 })
        {
            return query;
        }

        foreach (var criteriaGroup in criteria.Items.All)
        {
            if (criteriaGroup is not { All.Count: > 0 })
            {
                continue;
            }

            var awardsQuery =
                from e in GetAwardsProjection()
                select e.Item;

            foreach (var c in criteriaGroup.All)
            {
                awardsQuery = awardsQuery
                    .ApplyAllStringCriteria(c.AwardBody, nameof(Award.AwardBody))
                    .ApplyAllStringCriteria(c.Name, nameof(Award.Name))
                    .ApplyAllStringCriteria(c.Category, nameof(Award.Category))
                    .ApplyAllStringCriteria(c.Status, nameof(Award.Status))
                    .ApplyAllComparableCriteria(c.Year, nameof(Award.Year))
                    .ApplyAllStringCriteria(c.Language, nameof(Award.Language))
                    .ApplyAllStringCriteria(c.Country, nameof(Award.Country));
            }

            var awardContainerIds =
                from c in context.MetadataContributions
                join a in awardsQuery on c.Id equals a.MetadataContributionId
                select c.MetadataContainerId;

            query = criteria.Operator == CollectionCriteriaOperator.Some
                ? query.Where(m => awardContainerIds.Contains(m.ContainerId))
                : query.Where(m => !awardContainerIds.Contains(m.ContainerId));
        }

        return query;
    }

    private static IQueryable<MaterializedMetadata> ApplySort(
        IQueryable<MaterializedMetadata> query,
        SortParameter? sort)
    {
        if (sort is null)
        {
            return query.OrderBy(m => m.ContainerId);
        }
        if (sort.PropertyName is not
            "SortTitle" and not
            "Language" and not
            "Overview" and not
            "PublishDate" and not
            "Duration" and not
            "ProductionStatus" and not
            "Established" and not
            "Dissolved" and not
            "BirthDate" and not
            "DeathDate")
        {
            throw new ArgumentException($"Invalid sort property name: {sort.PropertyName}");
        }

        return sort.Direction == SortDirection.Ascending
            ? query.OrderBy(m => EF.Property<MaterializedMetadata>(m, sort.PropertyName))
            : query.OrderByDescending(m => EF.Property<MaterializedMetadata>(m, sort.PropertyName));
    }

    private IQueryable<MaterializedMetadata> GetMetadataProjection(string? preferredLanguage = null)
    {
        return
            from mc in context.MetadataContainers.AsNoTracking()
            select new MaterializedMetadata
            {
                ContainerId = mc.Id,
                Title = (
                    from c in mc.Contributions
                    where c.Title != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1), c.Ordinal
                    select c.Title).FirstOrDefault(),
                OriginalTitle = (
                    from c in mc.Contributions
                    where c.OriginalTitle != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1), c.Ordinal
                    select c.OriginalTitle).FirstOrDefault(),
                SortTitle = (
                    from c in mc.Contributions
                    where c.SortTitle != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1), c.Ordinal
                    select c.SortTitle).FirstOrDefault(),
                Language = (
                    from c in mc.Contributions
                    where c.Language != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1), c.Ordinal
                    select c.Language).FirstOrDefault(),
                Overview = (
                    from c in mc.Contributions
                    where c.Overview != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1), c.Ordinal
                    select c.Overview).FirstOrDefault(),
                PublishDate = (
                    from c in mc.Contributions
                    where c.PublishDate != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1), c.Ordinal
                    select c.PublishDate).FirstOrDefault(),
                Duration = (
                    from c in mc.Contributions
                    where c.Duration != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1), c.Ordinal
                    select c.Duration).FirstOrDefault(),
                ProductionStatus = (
                    from c in mc.Contributions
                    where c.ProductionStatus != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1), c.Ordinal
                    select c.ProductionStatus).FirstOrDefault(),
                Established = (
                    from c in mc.Contributions
                    where c.Established != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1), c.Ordinal
                    select c.Established).FirstOrDefault(),
                Dissolved = (
                    from c in mc.Contributions
                    where c.Dissolved != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1), c.Ordinal
                    select c.Dissolved).FirstOrDefault(),
                BirthDate = (
                    from c in mc.Contributions
                    where c.BirthDate != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1), c.Ordinal
                    select c.BirthDate).FirstOrDefault(),
                DeathDate = (
                    from c in mc.Contributions
                    where c.DeathDate != null
                    orderby preferredLanguage == null ? 0 : (c.Language == preferredLanguage ? 0 : 1), c.Ordinal
                    select c.DeathDate).FirstOrDefault()
            };
    }

    private IQueryable<Ranked<AlternateText>> GetAlternateTextsProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from a in c.AlternateTexts
            where c.Ordinal == (
                from c2 in context.MetadataContributions
                from a2 in c2.AlternateTexts
                where c2.MetadataContainerId == c.MetadataContainerId
                where a2.Kind.ToLower() == a.Kind.ToLower()
                where
                    (a2.Language == null && a.Language == null) ||
                    (a2.Language != null && a.Language != null && a2.Language.ToLower() == a.Language.ToLower())
                where a2.Text.ToLower() == a.Text.ToLower()
                select c2.Ordinal).Min()
            orderby c.Ordinal, a.Ordinal
            select new Ranked<AlternateText>(a, c.MetadataContainerId, c.Ordinal);
    }

    private IQueryable<Ranked<Award>> GetAwardsProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from a in c.Awards
            where c.Ordinal == (
                from c2 in context.MetadataContributions
                from a2 in c2.Awards
                where c2.MetadataContainerId == c.MetadataContainerId
                where a2.AwardBody.ToLower() == a.AwardBody.ToLower()
                where a2.Name.ToLower() == a.Name.ToLower()
                where a2.Year == a.Year
                where
                    (a2.Category == null && a.Category == null) ||
                    (a2.Category != null && a.Category != null && a2.Category.ToLower() == a.Category.ToLower())
                select c2.Ordinal).Min()
            orderby c.Ordinal, a.Ordinal
            select new Ranked<Award>(a, c.MetadataContainerId, c.Ordinal);
    }

    private IQueryable<Ranked<ContentRating>> GetContentRatingsProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from r in c.ContentRatings
            where c.Ordinal == (
                from c2 in context.MetadataContributions
                from r2 in c2.ContentRatings
                where c2.MetadataContainerId == c.MetadataContainerId
                where r2.RatingsBody.ToLower() == r.RatingsBody.ToLower()
                select c2.Ordinal).Min()
            orderby c.Ordinal, r.Ordinal
            select new Ranked<ContentRating>(r, c.MetadataContainerId, c.Ordinal);
    }

    private IQueryable<Ranked<CustomMetadataValue>> GetCustomMetadataProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from m in c.CustomMetadata
            where c.Ordinal == (
                from c2 in context.MetadataContributions
                from m2 in c2.CustomMetadata
                where c2.MetadataContainerId == c.MetadataContainerId
                where m2.Key.ToLower() == m.Key.ToLower()
                select c2.Ordinal).Min()
            orderby c.Ordinal
            select new Ranked<CustomMetadataValue>(m, c.MetadataContainerId, c.Ordinal);
    }

    private IQueryable<Ranked<ExternalId>> GetExternalIdsProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from e in c.ExternalIds
            where c.Ordinal == (
                from c2 in context.MetadataContributions
                from e2 in c2.ExternalIds
                where c2.MetadataContainerId == c.MetadataContainerId
                where e.Source != null
                    ? e2.Source != null && e2.Source.ToLower() == e.Source.ToLower()
                    : e2.Value.ToLower() == e.Value.ToLower()
                select c2.Ordinal).Min()
            orderby c.Ordinal, e.Ordinal
            select new Ranked<ExternalId>(e, c.MetadataContainerId, c.Ordinal);
    }

    private IQueryable<Ranked<Genre>> GetGenresProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from g in c.Genres
            where c.Ordinal == (
                from c2 in context.MetadataContributions
                from g2 in c2.Genres
                where c2.MetadataContainerId == c.MetadataContainerId
                where g2.Name.ToLower() == g.Name.ToLower()
                select c2.Ordinal).Min()
            orderby c.Ordinal, g.Ordinal
            select new Ranked<Genre>(g, c.MetadataContainerId, c.Ordinal);
    }

    private IQueryable<Ranked<Image>> GetImagesProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from i in c.Images
            where c.Ordinal == (
                from c2 in context.MetadataContributions
                from i2 in c2.Images
                where c2.MetadataContainerId == c.MetadataContainerId
                where i2.Kind.ToLower() == i.Kind.ToLower()
                select c2.Ordinal).Min()
            orderby c.Ordinal, i.Ordinal
            select new Ranked<Image>(i, c.MetadataContainerId, c.Ordinal);
    }

    private IQueryable<Ranked<Preview>> GetPreviewsProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from p in c.Previews
            where c.Ordinal == (
                from c2 in context.MetadataContributions
                from p2 in c2.Previews
                where c2.MetadataContainerId == c.MetadataContainerId
                where p2.Kind.ToLower() == p.Kind.ToLower()
                select c2.Ordinal).Min()
            orderby c.Ordinal, p.Ordinal
            select new Ranked<Preview>(p, c.MetadataContainerId, c.Ordinal);
    }

    private IQueryable<Ranked<Reference>> GetReferencesProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from r in c.References
            where c.Ordinal == (
                from c2 in context.MetadataContributions
                from r2 in c2.References
                where c2.MetadataContainerId == c.MetadataContainerId
                where r2.Source.ToLower() == r.Source.ToLower()
                where r2.Kind.ToLower() == r.Kind.ToLower()
                where
                    (r2.Language == null && r.Language == null) ||
                    (r2.Language != null && r.Language != null && r2.Language.ToLower() == r.Language.ToLower())
                select c2.Ordinal).Min()
            orderby c.Ordinal, r.Ordinal
            select new Ranked<Reference>(r, c.MetadataContainerId, c.Ordinal);
    }

    private IQueryable<Ranked<Relationship>> GetRelationshipsProjection()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from r in c.Relationships
            where c.Ordinal == (
                from c2 in context.MetadataContributions
                from r2 in c2.Relationships
                where c2.MetadataContainerId == c.MetadataContainerId
                where r2.Kind.ToLower() == r.Kind.ToLower()
                where r2.RelatedMetadataContainerId == r.RelatedMetadataContainerId
                where
                    (r2.Qualifier == null && r.Qualifier == null) ||
                    (r2.Qualifier != null && r.Qualifier != null && r2.Qualifier.ToLower() == r.Qualifier.ToLower())
                select c2.Ordinal).Min()
            orderby c.Ordinal, r.Ordinal
            select new Ranked<Relationship>(r, c.MetadataContainerId, c.Ordinal);
    }

}
