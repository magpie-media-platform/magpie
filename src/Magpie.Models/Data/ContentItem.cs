using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A media content item that Magpie can search for and download.
/// </summary>
public record ContentItem
{
    /// <summary>
    /// The unique id of the content item.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the content hierarchy item.
    /// </summary>
    public Guid ContentHierarchyItemId { get; set; }

    /// <summary>
    /// The fulfillment status of the content item.
    /// </summary>
    public FulfillmentStatus Fulfillment { get; set; }

    /// <summary>
    /// The latest known UTC import or replacement time of applicable primary media.
    /// </summary>
    public DateTime? FulfilledAt { get; set; }

    /// <summary>
    /// The total size of primary media files on this item and its descendants.
    /// </summary>
    public long PrimaryMediaSize { get; set; }

    /// <summary>
    /// The number of primary media files on this item and its descendants.
    /// </summary>
    public int PrimaryMediaFileCount { get; set; }

    /// <summary>
    /// The UTC time when current media files last supplied the stored state.
    /// </summary>
    public DateTime? MediaReconciledAt { get; set; }

    /// <summary>
    /// The best quality rank derived from the current primary media files.
    /// </summary>
    public int? CurrentFileQualityRank { get; set; }

    /// <summary>
    /// The UTC time of the request.
    /// </summary>
    public DateTime? RequestedAt { get; set; }

    /// <summary>
    /// The id of the user who made the request.
    /// </summary>
    public Guid? RequestedByUserId { get; set; }

    /// <summary>
    /// The id of the wishlist the request came through.
    /// </summary>
    public Guid? RequestedViaWishlistId { get; set; }

    /// <summary>
    /// The UTC time of the last search.
    /// </summary>
    public DateTime? DateLastSearched { get; set; }

    /// <summary>
    /// The UTC time the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }

    /// <summary>
    /// The related media files.
    /// </summary>
    public ICollection<MediaFile> MediaFiles { get; } = [];

    /// <summary>
    /// The status of the active acquisition.
    /// </summary>
    public DownloadStatus? ActiveAcquisitionStatus { get; set; }

    /// <summary>
    /// The quality status of the files against the effective quality tier, or <c>null</c> if not evaluated.
    /// </summary>
    public QualityStatus? QualityStatus { get; set; }
}
