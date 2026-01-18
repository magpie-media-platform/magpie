using System;

namespace Magpie.Models.Wishlists;

/// <summary>
/// Patch for <see cref="Wishlist"/>.
/// </summary>
public sealed record WishlistPatch
{
    /// <inheritdoc cref="Wishlist.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="Wishlist.Name" />
    public FieldPatch<string>? Name { get; init; }
    /// <inheritdoc cref="Wishlist.UserId" />
    public FieldPatch<Guid?>? UserId { get; init; }
    /// <inheritdoc cref="Wishlist.OwnerLock" />
    public FieldPatch<bool>? OwnerLock { get; init; }
    /// <inheritdoc cref="Wishlist.ContentFilterId" />
    public FieldPatch<Guid>? ContentFilterId { get; init; }
    /// <inheritdoc cref="Wishlist.QualityTierListId" />
    public FieldPatch<Guid?>? QualityTierListId { get; init; }
    /// <inheritdoc cref="Wishlist.Monitored" />
    public FieldPatch<bool>? Monitored { get; init; }
    /// <inheritdoc cref="Wishlist.Tags" />
    public SetPatch<string, string>? Tags { get; init; }
    /// <inheritdoc cref="Wishlist.SyncProvider" />
    public FieldPatch<string?>? SyncProvider { get; init; }
    /// <inheritdoc cref="Wishlist.SyncListId" />
    public FieldPatch<string?>? SyncListId { get; init; }
    /// <inheritdoc cref="Wishlist.SyncInterval" />
    public FieldPatch<TimeSpan?>? SyncInterval { get; init; }
}
