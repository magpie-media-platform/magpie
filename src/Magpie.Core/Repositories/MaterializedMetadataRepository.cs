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

            return new()
            {
                Items = materialized,
                PageNumber = paging?.PageNumber,
                PageSize = paging?.PageSize,
                TotalItems = totalItems
            };
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

            var genresByContainer = await (
                from g in GetGenresProjection()
                join c in context.MetadataContributions on g.MetadataContributionId equals c.Id
                group g by c.MetadataContainerId into grp
                select new { grp.Key, Items = grp.ToList() })
                .ToDictionaryAsync(k => k.Key, v => v.Items, cancellationToken)
                .ConfigureAwait(false);

            var externalIdsByContainer = await (
                from e in GetExternalIdsProjection()
                join c in context.MetadataContributions on e.MetadataContributionId equals c.Id
                group e by c.MetadataContainerId into grp
                select new { grp.Key, Items = grp.ToList() })
                .ToDictionaryAsync(k => k.Key, v => v.Items, cancellationToken)
                .ConfigureAwait(false);

            var contentRatingsByContainer = await (
                from r in GetContentRatingsProjection()
                join c in context.MetadataContributions on r.MetadataContributionId equals c.Id
                group r by c.MetadataContainerId into g
                select new { g.Key, Items = g.ToList() })
                .ToDictionaryAsync(k => k.Key, v => v.Items, cancellationToken)
                .ConfigureAwait(false);

            var awardsByContainer = await (
                from a in GetAwardsProjection()
                join c in context.MetadataContributions on a.MetadataContributionId equals c.Id
                group a by c.MetadataContainerId into g
                select new { g.Key, Items = g.ToList() })
                .ToDictionaryAsync(k => k.Key, v => v.Items, cancellationToken)
                .ConfigureAwait(false);

            var alternateTextsByContainer = await (
                from a in GetAlternateTextsProjection()
                join c in context.MetadataContributions on a.MetadataContributionId equals c.Id
                group a by c.MetadataContainerId into g
                select new { g.Key, Items = g.ToList() })
                .ToDictionaryAsync(k => k.Key, v => v.Items, cancellationToken)
                .ConfigureAwait(false);

            var referencesByContainer = await (
                from r in GetReferencesProjection()
                join c in context.MetadataContributions on r.MetadataContributionId equals c.Id
                group r by c.MetadataContainerId into grp
                select new { grp.Key, Items = grp.ToList() })
                .ToDictionaryAsync(k => k.Key, v => v.Items, cancellationToken)
                .ConfigureAwait(false);

            var relationshipsByContainer = await (
                from r in GetRelationshipsProjection()
                join c in context.MetadataContributions on r.MetadataContributionId equals c.Id
                group r by c.MetadataContainerId into grp
                select new { grp.Key, Items = grp.ToList() })
                .ToDictionaryAsync(k => k.Key, v => v.Items, cancellationToken)
                .ConfigureAwait(false);

            var imagesByContainer = await (
                from i in GetImagesProjection()
                join c in context.MetadataContributions on i.MetadataContributionId equals c.Id
                group i by c.MetadataContainerId into grp
                select new { grp.Key, Items = grp.ToList() })
                .ToDictionaryAsync(k => k.Key, v => v.Items, cancellationToken)
                .ConfigureAwait(false);

            var previewsByContainer = await (
                from p in GetPreviewsProjection()
                join c in context.MetadataContributions on p.MetadataContributionId equals c.Id
                group p by c.MetadataContainerId into grp
                select new { grp.Key, Items = grp.ToList() })
                .ToDictionaryAsync(k => k.Key, v => v.Items, cancellationToken)
                .ConfigureAwait(false);

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

        return query;
    }

    private IQueryable<MaterializedMetadata> ApplyAwardsCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<AwardCriteria> criteria)
    {
        if (criteria.Items is not { Count: > 0 })
        {
            return query;
        }

        foreach (var itemCriteria in criteria.Items)
        {
            var awardsQuery = GetAwardsProjection()
                .ApplyCriteriaSet(itemCriteria.AwardBody, nameof(Award.AwardBody))
                .ApplyCriteriaSet(itemCriteria.Name, nameof(Award.Name))
                .ApplyCriteriaSet(itemCriteria.Category, nameof(Award.Category))
                .ApplyCriteriaSet(itemCriteria.Status, nameof(Award.Status))
                .ApplyCriteriaSet(itemCriteria.Year, nameof(Award.Year))
                .ApplyCriteriaSet(itemCriteria.Language, nameof(Award.Language))
                .ApplyCriteriaSet(itemCriteria.Country, nameof(Award.Country));

            var containerIds =
                from c in context.MetadataContributions
                join a in awardsQuery on c.Id equals a.MetadataContributionId
                select c.MetadataContainerId;

            query = criteria.Operator == CollectionCriteriaOperator.Some
                ? query.Where(m => containerIds.Contains(m.ContainerId))
                : query.Where(m => !containerIds.Contains(m.ContainerId));
        }

        return query;
    }

    private IQueryable<MaterializedMetadata> ApplyGenresCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<PropertyCriterion<string>> criteria)
    {
        if (criteria.Items is not { Count: > 0 })
        {
            return query;
        }

        foreach (var itemCriteria in criteria.Items)
        {
            var genresQuery = GetGenresProjection().ApplyCriteriaSet(
                new CriteriaSet<PropertyCriterion<string>>()
                {
                    And = [itemCriteria]
                },
                nameof(Genre.Name));

            var containerIds =
                from c in context.MetadataContributions
                join g in genresQuery on c.Id equals g.MetadataContributionId
                select c.MetadataContainerId;

            query = criteria.Operator == CollectionCriteriaOperator.Some
                ? query.Where(m => containerIds.Contains(m.ContainerId))
                : query.Where(m => !containerIds.Contains(m.ContainerId));
        }

        return query;
    }

    private IQueryable<MaterializedMetadata> ApplyExternalIdsCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<ExternalIdCriteria> criteria)
    {
        if (criteria.Items is not { Count: > 0 })
        {
            return query;
        }

        foreach (var itemCriteria in criteria.Items)
        {
            var externalIdsQuery = GetExternalIdsProjection()
                .ApplyCriteriaSet(itemCriteria.Value, nameof(ExternalId.Value))
                .ApplyCriteriaSet(itemCriteria.Source, nameof(ExternalId.Source));

            var containerIds =
                from c in context.MetadataContributions
                join e in externalIdsQuery on c.Id equals e.MetadataContributionId
                select c.MetadataContainerId;

            query = criteria.Operator == CollectionCriteriaOperator.Some
                ? query.Where(m => containerIds.Contains(m.ContainerId))
                : query.Where(m => !containerIds.Contains(m.ContainerId));
        }

        return query;
    }

    private IQueryable<MaterializedMetadata> ApplyImagesCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<ImageCriteria> criteria)
    {
        if (criteria.Items is not { Count: > 0 })
        {
            return query;
        }

        foreach (var itemCriteria in criteria.Items)
        {
            var imagesQuery = GetImagesProjection()
                .ApplyCriteriaSet(itemCriteria.Kind, nameof(Image.Kind))
                .ApplyCriteriaSet(itemCriteria.MimeType, nameof(Image.MimeType))
                .ApplyCriteriaSet(itemCriteria.Source, nameof(Image.Source))
                .ApplyCriteriaSet(itemCriteria.Language, nameof(Image.Language))
                .ApplyCriteriaSet(itemCriteria.Width, nameof(Image.Width))
                .ApplyCriteriaSet(itemCriteria.Height, nameof(Image.Height));

            var containerIds =
                from c in context.MetadataContributions
                join i in imagesQuery on c.Id equals i.MetadataContributionId
                select c.MetadataContainerId;

            query = criteria.Operator == CollectionCriteriaOperator.Some
                ? query.Where(m => containerIds.Contains(m.ContainerId))
                : query.Where(m => !containerIds.Contains(m.ContainerId));
        }

        return query;
    }

    private IQueryable<MaterializedMetadata> ApplyPreviewsCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<PreviewMetadataCriteria> criteria)
    {
        if (criteria.Items is not { Count: > 0 })
        {
            return query;
        }

        foreach (var itemCriteria in criteria.Items)
        {
            var previewsQuery = GetPreviewsProjection()
                .ApplyCriteriaSet(itemCriteria.MimeType, nameof(Preview.MimeType))
                .ApplyCriteriaSet(itemCriteria.Source, nameof(Preview.Source))
                .ApplyCriteriaSet(itemCriteria.Title, nameof(Preview.Title))
                .ApplyCriteriaSet(itemCriteria.ReleaseDate, nameof(Preview.ReleaseDate))
                .ApplyCriteriaSet(itemCriteria.Language, nameof(Preview.Language));

            var containerIds =
                from c in context.MetadataContributions
                join p in previewsQuery on c.Id equals p.MetadataContributionId
                select c.MetadataContainerId;

            query = criteria.Operator == CollectionCriteriaOperator.Some
                ? query.Where(m => containerIds.Contains(m.ContainerId))
                : query.Where(m => !containerIds.Contains(m.ContainerId));
        }

        return query;
    }

    private IQueryable<MaterializedMetadata> ApplyContentRatingsCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<ContentRatingCriteria> criteria)
    {
        if (criteria.Items is not { Count: > 0 })
        {
            return query;
        }

        foreach (var itemCriteria in criteria.Items)
        {
            var ratingsQuery = GetContentRatingsProjection()
                .ApplyCriteriaSet(itemCriteria.RatingsBody, nameof(ContentRating.RatingsBody))
                .ApplyCriteriaSet(itemCriteria.Value, nameof(ContentRating.Value));

            var containerIds =
                from c in context.MetadataContributions
                join r in ratingsQuery on c.Id equals r.MetadataContributionId
                select c.MetadataContainerId;

            query = criteria.Operator == CollectionCriteriaOperator.Some
                ? query.Where(m => containerIds.Contains(m.ContainerId))
                : query.Where(m => !containerIds.Contains(m.ContainerId));
        }

        return query;
    }

    private IQueryable<MaterializedMetadata> ApplyAlternateTextsCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<AlternateTextCriteria> criteria)
    {
        if (criteria.Items is not { Count: > 0 })
        {
            return query;
        }

        foreach (var itemCriteria in criteria.Items)
        {
            var textsQuery = GetAlternateTextsProjection()
                .ApplyCriteriaSet(itemCriteria.Kind, nameof(AlternateText.Kind))
                .ApplyCriteriaSet(itemCriteria.Language, nameof(AlternateText.Language))
                .ApplyCriteriaSet(itemCriteria.Text, nameof(AlternateText.Text));

            var containerIds =
                from c in context.MetadataContributions
                join a in textsQuery on c.Id equals a.MetadataContributionId
                select c.MetadataContainerId;

            query = criteria.Operator == CollectionCriteriaOperator.Some
                ? query.Where(m => containerIds.Contains(m.ContainerId))
                : query.Where(m => !containerIds.Contains(m.ContainerId));
        }

        return query;
    }

    private IQueryable<MaterializedMetadata> ApplyReferencesCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<ReferenceMetadataCriteria> criteria)
    {
        if (criteria.Items is not { Count: > 0 })
        {
            return query;
        }

        foreach (var itemCriteria in criteria.Items)
        {
            var refsQuery = GetReferencesProjection()
                .ApplyCriteriaSet(itemCriteria.Kind, nameof(Reference.Kind))
                .ApplyCriteriaSet(itemCriteria.DisplayName, nameof(Reference.DisplayName))
                .ApplyCriteriaSet(itemCriteria.Language, nameof(Reference.Language));

            var containerIds =
                from c in context.MetadataContributions
                join r in refsQuery on c.Id equals r.MetadataContributionId
                select c.MetadataContainerId;

            query = criteria.Operator == CollectionCriteriaOperator.Some
                ? query.Where(m => containerIds.Contains(m.ContainerId))
                : query.Where(m => !containerIds.Contains(m.ContainerId));
        }

        return query;
    }

    private IQueryable<MaterializedMetadata> ApplyRelationshipsCriteria(
        IQueryable<MaterializedMetadata> query,
        CollectionCriteria<RelationshipCriteria> criteria)
    {
        if (criteria.Items is not { Count: > 0 })
        {
            return query;
        }

        foreach (var itemCriteria in criteria.Items)
        {
            var relationshipsQuery = GetRelationshipsProjection()
                .ApplyCriteriaSet(itemCriteria.Kind, nameof(Relationship.Kind))
                .ApplyCriteriaSet(itemCriteria.Qualifier, nameof(Relationship.Qualifier));

            var containerIds =
                from c in context.MetadataContributions
                join r in relationshipsQuery on c.Id equals r.MetadataContributionId
                select c.MetadataContainerId;

            query = criteria.Operator == CollectionCriteriaOperator.Some
                ? query.Where(m => containerIds.Contains(m.ContainerId))
                : query.Where(m => !containerIds.Contains(m.ContainerId));
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

    private IQueryable<AlternateText> GetAlternateTextsProjection()
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
            select a;
    }

    private IQueryable<Award> GetAwardsProjection()
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
            select a;
    }

    private IQueryable<ContentRating> GetContentRatingsProjection()
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
            select r;
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

    private IQueryable<ExternalId> GetExternalIdsProjection()
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
            select e;
    }

    private IQueryable<Genre> GetGenresProjection()
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
            select g;
    }

    private IQueryable<Image> GetImagesProjection()
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
            select i;
    }

    private IQueryable<Preview> GetPreviewsProjection()
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
            select p;
    }

    private IQueryable<Reference> GetReferencesProjection()
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
            select r;
    }

    private IQueryable<Relationship> GetRelationshipsProjection()
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
            select r;
    }

}
