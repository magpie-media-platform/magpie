using System;
using System.Collections.Generic;

namespace Magpie.Models.Wishlists;

/// <summary>
/// A wishlist that uses a filter to discover and monitor content.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="UserId">The user who owns this wishlist.</param>
/// <param name="OwnerLock">When true, only the owner can update the wishlist.</param>
/// <param name="ContentFilterId">The content filter that defines what to look for.</param>
/// <param name="QualityTierListId">The quality tier list to apply to discovered items.</param>
/// <param name="Monitored">Whether discovered items should be monitored.</param>
/// <param name="Tags">Tags to apply to discovered items.</param>
/// <param name="SyncProvider">The sync provider for external lists, or null for local-only.</param>
/// <param name="SyncListId">The external list identifier for the sync provider.</param>
/// <param name="SyncInterval">The interval between syncs, or null for manual sync only.</param>
/// <param name="LastSyncedAt">The last time this wishlist was synced.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record Wishlist(
    Guid Id,
    string Name,
    Guid? UserId,
    bool OwnerLock,
    Guid ContentFilterId,
    Guid? QualityTierListId,
    bool Monitored,
    IReadOnlyList<string> Tags,
    string? SyncProvider,
    string? SyncListId,
    TimeSpan? SyncInterval,
    DateTimeOffset? LastSyncedAt,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
