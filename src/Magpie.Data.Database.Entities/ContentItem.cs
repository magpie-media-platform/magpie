using System;
using System.Collections.Generic;

using Magpie.Models.Content;

namespace Magpie.Data.Database.Entities;

public record ContentItem
{
    public Guid Id { get; set; }
    public Guid ContentHierarchyId { get; set; }
    public Guid? QualityTierListId { get; set; }
    public MonitoringIntent Monitoring { get; set; }
    public FulfillmentStatus Fulfillment { get; set; }
    public DateTimeOffset? DateLastSearched { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public Guid? RequestedViaWishlistId { get; set; }
    public DateTimeOffset? RequestedAt { get; set; }
    public DateTimeOffset? ImportedAt { get; set; }
    public Guid? ImportedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public ContentHierarchyLevel? ContentHierarchyLevel { get; set; }
    public QualityTierList? QualityTierList { get; set; }
    public User? RequestedByUser { get; set; }
    public Wishlist? RequestedViaWishlist { get; set; }
    public User? ImportedByUser { get; set; }
    public ICollection<MediaFile> MediaFiles { get; } = [];
    public ICollection<ContentItemTag> Tags { get; } = [];
    public ICollection<Download> Downloads { get; } = [];
}
