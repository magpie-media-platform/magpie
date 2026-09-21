using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Patch;
using Magpie.Sdk.Services;

namespace Magpie.Core;

internal sealed class WishlistService(
    IWishlistRepository wishlists,
    IWishlistDiscoveredRepository discovered,
    IContentFilterRepository contentFilters,
    ITransactionManager transactions) : IWishlistService
{
    private static bool CanView(Wishlist wishlist, Guid callerUserId, bool callerIsAdmin) =>
        callerIsAdmin || wishlist.OwnerUserId is null || wishlist.OwnerUserId == callerUserId;

    private static bool CanWrite(Wishlist wishlist, Guid callerUserId, bool callerIsAdmin) =>
        wishlist.OwnerUserId is null
            ? callerIsAdmin
            : wishlist.OwnerUserId == callerUserId || (callerIsAdmin && !wishlist.OwnerLock);

    private static string BackingFilterName(string wishlistName) => $"{wishlistName} (wishlist)";

    private async Task<bool> OwnsFilterExclusivelyAsync(
        Wishlist wishlist,
        ContentFilter? filter,
        CancellationToken cancellationToken)
    {
        if (filter is null || filter.OwnerUserId != wishlist.OwnerUserId)
        {
            return false;
        }

        var all = await wishlists.ListAllAsync(cancellationToken).ConfigureAwait(false);

        return all.All(other => other.Id == wishlist.Id || other.ContentFilterId != filter.Id);
    }

    public async Task<IReadOnlyList<Wishlist>> ListVisibleAsync(
        bool? monitored,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        var all = await wishlists.ListAllAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. all.Where(w =>
                (w.OwnerUserId is null || w.OwnerUserId == callerUserId) &&
                (monitored is null || w.Enabled == monitored))
        ];
    }

    public async Task<IReadOnlyList<Wishlist>> ListOwnAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        return await wishlists.QueryAllAsync(
            new WishlistQuery { OwnerUserId = ownerUserId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<Wishlist?> GetAsync(
        Guid wishlistId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var wishlist = await wishlists.GetByIdAsync(wishlistId, cancellationToken).ConfigureAwait(false);

        return wishlist is not null && CanView(wishlist, callerUserId, callerIsAdmin) ? wishlist : null;
    }

    public async Task<Wishlist> CreateAsync(
        WishlistCreate create,
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var created = await transactions.ExecuteWithTransactionAsync(
            async token =>
            {
                var contentFilterId = create.ContentFilterId ?? Guid.Empty;

                if (create.Criteria is not null)
                {
                    var backingFilter = await contentFilters.CreateAsync(
                        new ContentFilter
                        {
                            Id = Guid.NewGuid(),
                            Name = BackingFilterName(create.Name),
                            Criteria = create.Criteria,
                            OwnerUserId = ownerUserId,
                            OwnerLock = false
                        },
                        ownerUserId,
                        token).ConfigureAwait(false);

                    contentFilterId = backingFilter.Id;

                    if (create.SaveAsReusableFilter)
                    {
                        await contentFilters.CreateAsync(
                            new ContentFilter
                            {
                                Id = Guid.NewGuid(),
                                Name = create.Name,
                                Criteria = create.Criteria,
                                OwnerUserId = null,
                                OwnerLock = false
                            },
                            ownerUserId,
                            token).ConfigureAwait(false);
                    }
                }
                else if (contentFilterId != Guid.Empty)
                {
                    var referenced = await contentFilters.GetByIdAsync(contentFilterId, token)
                        .ConfigureAwait(false);
                    if (referenced is null ||
                        (referenced.OwnerUserId is not null && referenced.OwnerUserId != ownerUserId))
                    {
                        throw new MagpieException("The content filter was not found.");
                    }
                }

                return await wishlists.CreateAsync(
                    new Wishlist
                    {
                        Id = Guid.NewGuid(),
                        Name = create.Name,
                        ContentFilterId = contentFilterId,
                        QualityTierListId = create.QualityTierListId,
                        RootFolderId = create.RootFolderId,
                        OwnerUserId = ownerUserId,
                        OwnerLock = create.OwnerLock,
                        Enabled = create.Enabled,
                        Monitoring = create.Monitoring
                    },
                    ownerUserId,
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return created;
    }

    public async Task<bool> PatchAsync(
        Guid wishlistId,
        WishlistPatch patch,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var existing = await wishlists.GetByIdAsync(wishlistId, cancellationToken).ConfigureAwait(false);
        if (existing is null || !CanWrite(existing, callerUserId, callerIsAdmin))
        {
            return false;
        }

        if (patch.Criteria is null)
        {
            return await wishlists
                .PatchAsync(patch with { Id = wishlistId }, callerUserId, cancellationToken)
                .ConfigureAwait(false);
        }

        var criteria = patch.Criteria.Value;
        var effectiveName = patch.Name?.Value ?? existing.Name;

        return await transactions.ExecuteWithTransactionAsync(
            async token =>
            {
                var contentFilterId = existing.ContentFilterId;
                var backing = contentFilterId == Guid.Empty
                    ? null
                    : await contentFilters.GetByIdAsync(contentFilterId, token).ConfigureAwait(false);

                if (!await OwnsFilterExclusivelyAsync(existing, backing, token).ConfigureAwait(false))
                {
                    var backingFilter = await contentFilters.CreateAsync(
                        new ContentFilter
                        {
                            Id = Guid.NewGuid(),
                            Name = BackingFilterName(effectiveName),
                            Criteria = criteria,
                            OwnerUserId = existing.OwnerUserId,
                            OwnerLock = false
                        },
                        callerUserId,
                        token).ConfigureAwait(false);

                    contentFilterId = backingFilter.Id;
                }
                else
                {
                    await contentFilters.PatchAsync(
                        new ContentFilterPatch
                        {
                            Id = contentFilterId,
                            Criteria = new ScalarPatch<ContentFilterCriteria> { Value = criteria }
                        },
                        callerUserId,
                        token).ConfigureAwait(false);
                }

                if (patch.SaveAsReusableFilter == true)
                {
                    await contentFilters.CreateAsync(
                        new ContentFilter
                        {
                            Id = Guid.NewGuid(),
                            Name = effectiveName,
                            Criteria = criteria,
                            OwnerUserId = null,
                            OwnerLock = false
                        },
                        callerUserId,
                        token).ConfigureAwait(false);
                }

                return await wishlists.PatchAsync(
                    patch with
                    {
                        Id = wishlistId,
                        Criteria = null,
                        SaveAsReusableFilter = null,
                        ContentFilterId = new ScalarPatch<Guid?> { Value = contentFilterId }
                    },
                    callerUserId,
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(
        Guid wishlistId,
        bool deleteFilter,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var existing = await wishlists.GetByIdAsync(wishlistId, cancellationToken).ConfigureAwait(false);
        if (existing is null || !CanWrite(existing, callerUserId, callerIsAdmin))
        {
            return false;
        }

        if (!deleteFilter || existing.ContentFilterId == Guid.Empty)
        {
            return await wishlists.DeleteAsync(wishlistId, callerUserId, cancellationToken).ConfigureAwait(false);
        }

        return await transactions.ExecuteWithTransactionAsync(
            async token =>
            {
                var removed = await wishlists.DeleteAsync(wishlistId, callerUserId, token).ConfigureAwait(false);
                if (removed)
                {
                    await contentFilters
                        .DeleteAsync(existing.ContentFilterId, callerUserId, token)
                        .ConfigureAwait(false);
                }

                return removed;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<WishlistDiscoveredLedger?> GetDiscoveredAsync(
        Guid wishlistId,
        WishlistDiscoveredQuery query,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var wishlist = await wishlists.GetByIdAsync(wishlistId, cancellationToken).ConfigureAwait(false);
        if (wishlist is null || !CanView(wishlist, callerUserId, callerIsAdmin))
        {
            return null;
        }

        var page = await discovered.ListEnrichedAsync(wishlistId, query, cancellationToken).ConfigureAwait(false);
        var counts = await discovered
            .GetCountsAsync(wishlistId, query.TitleSearch, cancellationToken)
            .ConfigureAwait(false);

        return new WishlistDiscoveredLedger { Page = page, Counts = counts };
    }
}
