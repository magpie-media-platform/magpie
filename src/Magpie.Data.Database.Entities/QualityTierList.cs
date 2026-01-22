using System;
using System.Collections.Generic;

namespace Magpie.Data.Database.Entities;

public record QualityTierList
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public bool UpgradeAllowed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public ICollection<QualityTier> Tiers { get; } = [];
    public ICollection<QualityTierListTag> Tags { get; } = [];
    public ICollection<ContentItem> ContentItems { get; } = [];
    public ICollection<Wishlist> Wishlists { get; } = [];
}
