using System;
using System.Collections.Generic;

namespace Magpie.Data.Database.Entities;

public class Wishlist
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Guid? UserId { get; set; }
    public bool OwnerLock { get; set; }
    public Guid ContentFilterId { get; set; }
    public Guid? QualityTierListId { get; set; }
    public required bool Monitored { get; set; }
    public string? SyncProvider { get; set; }
    public string? SyncListId { get; set; }
    public TimeSpan? SyncInterval { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public User? User { get; set; }
    public required ContentFilter ContentFilter { get; set; }
    public QualityTierList? QualityTierList { get; set; }
    public ICollection<WishlistTag> Tags { get; } = [];
}
