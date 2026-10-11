using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Paging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class WishlistDiscoveredRepository(
    MagpieDbContext context,
    TimeProvider time,
    ILogger<WishlistDiscoveredRepository> logger) : IWishlistDiscoveredRepository
{
    private static IQueryable<EnrichedProjection> ApplyInLibraryFilter(
        IQueryable<EnrichedProjection> query,
        bool? inLibrary)
    {
        return inLibrary switch
        {
            true => query.Where(r => r.LinkId != null),
            false => query.Where(r => r.LinkId == null),
            _ => query,
        };
    }

    private static IQueryable<EnrichedProjection> ApplyStatusFilter(
        IQueryable<EnrichedProjection> query,
        WishlistDiscoveredItemStatus? status)
    {
        return status switch
        {
            WishlistDiscoveredItemStatus.Missing =>
                query.Where(r => r.LinkId != null && r.FulfilledContainerId == null),
            WishlistDiscoveredItemStatus.Found =>
                query.Where(r => r.LinkId != null && r.FulfilledContainerId != null),
            _ => query,
        };
    }

    private static IQueryable<EnrichedProjection> ApplySort(
        IQueryable<EnrichedProjection> query,
        WishlistDiscoveredSort sort,
        bool? descending)
    {
        return sort switch
        {
            WishlistDiscoveredSort.Added => (descending ?? true) ? query
                    .OrderBy(r => r.AddedAt == null)
                    .ThenByDescending(r => r.AddedAt)
                    .ThenByDescending(r => r.DiscoveredAt)
                    .ThenBy(r => r.DiscoveredId)
                : query
                    .OrderBy(r => r.AddedAt == null)
                    .ThenBy(r => r.AddedAt)
                    .ThenByDescending(r => r.DiscoveredAt)
                    .ThenBy(r => r.DiscoveredId),
            WishlistDiscoveredSort.PublishDate => (descending ?? true) ? query
                    .OrderBy(r => r.PublishDate == null)
                    .ThenByDescending(r => r.PublishDate)
                    .ThenByDescending(r => r.DiscoveredAt)
                    .ThenBy(r => r.DiscoveredId)
                : query
                    .OrderBy(r => r.PublishDate == null)
                    .ThenBy(r => r.PublishDate)
                    .ThenByDescending(r => r.DiscoveredAt)
                    .ThenBy(r => r.DiscoveredId),
            WishlistDiscoveredSort.Title => (descending ?? false) ? query
                    .OrderBy(r => r.Title == null)
                    .ThenByDescending(r => r.Title)
                    .ThenByDescending(r => r.DiscoveredAt)
                    .ThenBy(r => r.DiscoveredId)
                : query
                    .OrderBy(r => r.Title == null)
                    .ThenBy(r => r.Title)
                    .ThenByDescending(r => r.DiscoveredAt)
                    .ThenBy(r => r.DiscoveredId),
            WishlistDiscoveredSort.Status => (descending ?? true) ? query
                    .OrderBy(r => r.LinkId == null)
                    .ThenByDescending(r => r.FulfilledContainerId != null)
                    .ThenByDescending(r => r.DiscoveredAt)
                    .ThenBy(r => r.DiscoveredId)
                : query
                    .OrderBy(r => r.LinkId == null)
                    .ThenBy(r => r.FulfilledContainerId != null)
                    .ThenByDescending(r => r.DiscoveredAt)
                    .ThenBy(r => r.DiscoveredId),
            _ => (descending ?? true) ? query
                    .OrderByDescending(r => r.DiscoveredAt)
                    .ThenBy(r => r.DiscoveredId)
                : query
                    .OrderBy(r => r.DiscoveredAt)
                    .ThenBy(r => r.DiscoveredId),
        };
    }

    private static WishlistDiscoveredEnrichedRow ToEnrichedRow(
        EnrichedProjection p,
        IReadOnlyDictionary<Guid, string> postersByContainer)
    {
        return new()
        {
            DiscoveredId = p.DiscoveredId,
            Source = p.Source,
            ExternalId = p.ExternalId,
            Title = p.Title,
            DiscoveredAt = p.DiscoveredAt,
            LinkId = p.LinkId,
            PosterUrl = p.LinkId is null
                ? null
                : p.ContainerId is not null && postersByContainer.TryGetValue(p.ContainerId.Value, out var url)
                    ? url
                    : null,
            PublishDate = p.LinkId is null ? null : p.PublishDate,
            PublishDatePrecision = p.LinkId is null ? null : p.PublishDatePrecision,
            AddedAt = p.AddedAt,
            Status = p.LinkId is null
                ? default
                : p.Fulfilled
                    ? WishlistDiscoveredItemStatus.Found
                    : WishlistDiscoveredItemStatus.Missing,
        };
    }

    public async Task<IReadOnlyCollection<WishlistDiscovered>> ListAsync(
        Guid wishlistId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.WishlistDiscovered
                .AsNoTracking()
                .Where(discovered => discovered.WishlistId == wishlistId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The wishlist discovery repository failed to list discovered keys for wishlist {WishlistId}.",
                wishlistId);
            throw;
        }
    }

    public async Task RecordDiscoveredAsync(
        Guid wishlistId,
        IEnumerable<WishlistDiscovered> candidates,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var byKey = new Dictionary<(string ExternalIdSource, string ExternalId), string?>();
            foreach (var candidate in candidates)
            {
                byKey[(candidate.ExternalIdSource, candidate.ExternalId)] = candidate.Title;
            }

            if (byKey.Count == 0)
            {
                return;
            }

            var discovered = await ListAsync(wishlistId, cancellationToken).ConfigureAwait(false);
            var existing = new HashSet<(string ExternalIdSource, string ExternalId)>(
                discovered.Select(key => (key.ExternalIdSource, key.ExternalId)));

            var now = time.GetUtcNow().UtcDateTime;
            var toInsert = (
                from pair in byKey
                where !existing.Contains(pair.Key)
                select new WishlistDiscovered
                {
                    Id = Guid.NewGuid(),
                    WishlistId = wishlistId,
                    ExternalIdSource = pair.Key.ExternalIdSource,
                    ExternalId = pair.Key.ExternalId,
                    Title = pair.Value,
                    DiscoveredAt = now,
                })
                .ToList();

            if (toInsert.Count == 0)
            {
                return;
            }

            context.WishlistDiscovered.AddRange(toInsert);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The wishlist discovery repository failed to record discovered candidates for wishlist {WishlistId}.",
                wishlistId);
            throw;
        }
    }

    public async Task<PagedList<WishlistDiscoveredEnrichedRow>> ListEnrichedAsync(
        Guid wishlistId,
        WishlistDiscoveredQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projected = await BuildEnrichedQueryAsync(wishlistId, query.TitleSearch, cancellationToken)
                .ConfigureAwait(false);

            projected = ApplyInLibraryFilter(projected, query.InLibrary);
            projected = ApplyStatusFilter(projected, query.Status);

            var total = await projected.CountAsync(cancellationToken).ConfigureAwait(false);

            projected = ApplySort(projected, query.Sort, query.SortDescending);

            var items = await projected
                .Skip((query.Paging.PageNumber - 1) * query.Paging.PageSize)
                .Take(query.Paging.PageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var posters = await ResolvePostersAsync(items, cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = [.. items.Select(p => ToEnrichedRow(p, posters))],
                PageNumber = query.Paging.PageNumber,
                PageSize = query.Paging.PageSize,
                TotalItems = total,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The wishlist discovery repository failed to list enriched discovered rows for wishlist {WishlistId}.",
                wishlistId);
            throw;
        }
    }

    public async Task<WishlistDiscoveredCounts> GetCountsAsync(
        Guid wishlistId,
        string? titleSearch,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projected = await BuildEnrichedQueryAsync(wishlistId, titleSearch, cancellationToken)
                .ConfigureAwait(false);

            var total = await projected.CountAsync(cancellationToken).ConfigureAwait(false);
            var inLibrary = await projected
                .CountAsync(r => r.LinkId != null, cancellationToken)
                .ConfigureAwait(false);
            var missing = await projected
                .CountAsync(r => r.LinkId != null && r.FulfilledContainerId == null, cancellationToken)
                .ConfigureAwait(false);

            return new()
            {
                TotalDiscovered = total,
                InLibrary = inLibrary,
                NotInLibrary = total - inLibrary,
                Missing = missing,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The wishlist discovery repository failed to count discovered rows for wishlist {WishlistId}.",
                wishlistId);
            throw;
        }
    }

    private async Task<IQueryable<EnrichedProjection>> BuildEnrichedQueryAsync(
        Guid wishlistId,
        string? titleSearch,
        CancellationToken cancellationToken)
    {
        var discovered = context.WishlistDiscovered
            .AsNoTracking()
            .Where(s => s.WishlistId == wishlistId);

        if (!string.IsNullOrWhiteSpace(titleSearch))
        {
            var term = titleSearch.Trim();
            discovered = discovered.Where(s => s.Title != null && EF.Functions.Like(s.Title, "%" + term + "%"));
        }

        var resolutions =
            from eid in context.ExternalIds.AsNoTracking()
            join mc in context.MetadataContributions.AsNoTracking()
                on eid.MetadataContributionId equals mc.Id
            join hi in context.ContentHierarchyItems.AsNoTracking()
                on mc.MetadataContainerId equals hi.MetadataContainerId
            where eid.Source != null
            group hi.Id by new { eid.Source, eid.Value } into g
            select new { g.Key.Source, g.Key.Value, HierarchyItemId = (Guid?)g.Min() };

        var linked = await (
            from s in discovered
            join res in resolutions
                on new { Source = (string?)s.ExternalIdSource, Value = s.ExternalId }
                equals new { res.Source, res.Value }
            select new { s.Id, s.DiscoveredAt, res.HierarchyItemId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hidden = (
            from row in linked
            group row by row.HierarchyItemId into item
            from duplicate in item.OrderBy(row => row.DiscoveredAt).ThenBy(row => row.Id).Skip(1)
            select duplicate.Id).ToList();
        discovered = discovered.Where(s => !hidden.Contains(s.Id));

        var publishByContainer =
            from mc in context.MetadataContributions.AsNoTracking()
            where mc.PublishDate != null
            group mc by mc.MetadataContainerId into g
            select new
            {
                ContainerId = g.Key,
                PublishDate = g.Min(c => c.PublishDate),
                PublishDatePrecision = g
                    .OrderBy(c => c.PublishDate)
                    .ThenBy(c => c.Ordinal)
                    .ThenBy(c => c.Id)
                    .Select(c => c.PublishDatePrecision)
                    .FirstOrDefault(),
            };

        var fulfilledContainers =
            from ci in context.ContentItems.AsNoTracking()
            join hi in context.ContentHierarchyItems.AsNoTracking()
                on ci.ContentHierarchyItemId equals hi.Id
            where ci.Fulfillment != FulfillmentStatus.Missing
            group hi.MetadataContainerId by hi.MetadataContainerId into g
            select new { ContainerId = (Guid?)g.Key };

        return
            from s in discovered
            join res in resolutions
                on new { Source = (string?)s.ExternalIdSource, Value = s.ExternalId }
                equals new { res.Source, res.Value } into resJoin
            from res in resJoin.DefaultIfEmpty()
            join hi in context.ContentHierarchyItems.AsNoTracking()
                on res.HierarchyItemId equals hi.Id into hiJoin
            from hi in hiJoin.DefaultIfEmpty()
            join pub in publishByContainer
                on hi == null ? (Guid?)null : hi.MetadataContainerId equals pub.ContainerId into pubJoin
            from pub in pubJoin.DefaultIfEmpty()
            join ful in fulfilledContainers
                on hi == null ? null : hi.MetadataContainerId equals ful.ContainerId into fulJoin
            from ful in fulJoin.DefaultIfEmpty()
            select new EnrichedProjection
            {
                DiscoveredId = s.Id,
                Source = s.ExternalIdSource,
                ExternalId = s.ExternalId,
                Title = s.Title,
                DiscoveredAt = s.DiscoveredAt,

                LinkId = hi == null ? null : hi.Id,
                ContainerId = hi == null ? null : hi.MetadataContainerId,
                AddedAt = hi == null ? null : hi.CreatedAt,
                PublishDate = hi == null ? null : pub.PublishDate,
                PublishDatePrecision = hi == null ? null : pub.PublishDatePrecision,

                FulfilledContainerId = ful == null ? null : ful.ContainerId,
            };
    }

    private async Task<Dictionary<Guid, string>> ResolvePostersAsync(
        IReadOnlyList<EnrichedProjection> page,
        CancellationToken cancellationToken)
    {
        var containerIds = (from p in page where p.ContainerId is not null select p.ContainerId!.Value)
            .Distinct()
            .ToList();
        if (containerIds.Count == 0)
        {
            return [];
        }

        var images = await (
            from mc in context.MetadataContributions.AsNoTracking()
            where containerIds.Contains(mc.MetadataContainerId)
            from img in mc.Images
            select new
            {
                mc.MetadataContainerId,
                ContributionOrdinal = mc.Ordinal,
                img.Type,
                img.Ordinal,
                Url = img.Url,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return images
            .GroupBy(x => x.MetadataContainerId)
            .ToDictionary(
                g => g.Key,
                g => (
                    from image in g
                    orderby string.Equals(image.Type, "poster", StringComparison.OrdinalIgnoreCase) descending,
                        string.Equals(image.Type, "primary", StringComparison.OrdinalIgnoreCase) descending,
                        string.Equals(image.Type, "profile", StringComparison.OrdinalIgnoreCase) descending,
                        image.ContributionOrdinal,
                        image.Ordinal
                    select image.Url.ToString()).First());
    }

    private sealed record EnrichedProjection
    {
        public Guid DiscoveredId { get; init; }

        public required string Source { get; init; }

        public required string ExternalId { get; init; }

        public string? Title { get; init; }

        public DateTime DiscoveredAt { get; init; }

        public Guid? LinkId { get; init; }

        public Guid? ContainerId { get; init; }

        public DateTime? PublishDate { get; init; }

        public TemporalPrecision? PublishDatePrecision { get; init; }

        public DateTime? AddedAt { get; init; }

        public Guid? FulfilledContainerId { get; init; }

        public bool Fulfilled => FulfilledContainerId is not null;
    }
}
