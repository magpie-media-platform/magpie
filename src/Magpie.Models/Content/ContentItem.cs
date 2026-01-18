using System;
using System.Collections.Generic;

namespace Magpie.Models.Content;

/// <summary>
/// A content item with associated media files.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="QualityTierListId">The quality tier list to use when searching for this item.</param>
/// <param name="Monitoring">The monitoring intent for this item.</param>
/// <param name="Fulfillment">The fulfillment status of this item.</param>
/// <param name="DateLastSearched">The date the item was last searched for new releases.</param>
/// <param name="MediaFiles">Media files associated with this item.</param>
/// <param name="Tags">Tags applied to this item.</param>
/// <param name="RequestedByUserId">The user who requested this content.</param>
/// <param name="RequestedViaWishlistId">The wishlist that triggered the request.</param>
/// <param name="RequestedAt">When the content was requested.</param>
/// <param name="ImportedAt">When the content was imported from existing files.</param>
/// <param name="ImportedByUserId">The user who imported the content.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record ContentItem(
    Guid Id,
    Guid? QualityTierListId,
    MonitoringIntent Monitoring,
    FulfillmentStatus Fulfillment,
    DateTimeOffset? DateLastSearched,
    IReadOnlyList<MediaFile> MediaFiles,
    IReadOnlyList<string> Tags,
    Guid? RequestedByUserId,
    Guid? RequestedViaWishlistId,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? ImportedAt,
    Guid? ImportedByUserId,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
