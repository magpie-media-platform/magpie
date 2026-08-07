using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class WishlistEventBuilder
{
    public static MagpieEvent BuildWishlistCreatedEvent(Guid wishlistId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.WishlistId] = wishlistId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.WishlistCreated, context, default, default, null);
    }

    public static MagpieEvent BuildWishlistDeletedEvent(Guid wishlistId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.WishlistId] = wishlistId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.WishlistDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildWishlistItemMatchedEvent(Guid wishlistItemId, Guid contentItemId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.WishlistItemId] = wishlistItemId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.ContentItemId] = contentItemId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.WishlistItemMatched, context, default, default, null);
    }

    public static MagpieEvent BuildWishlistUpdatedEvent(Guid wishlistId, IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.WishlistId] = wishlistId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.WishlistChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.WishlistUpdated, context, default, default, null);
    }
}
