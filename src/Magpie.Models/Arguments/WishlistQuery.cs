using System;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for wishlists.
/// </summary>
public sealed record WishlistQuery
{
    /// <summary>
    /// The id of the owner whose wishlists to match, or <c>null</c> to not constrain by owner.
    /// </summary>
    public Guid? OwnerUserId { get; init; }

    /// <summary>
    /// Whether to match only monitored wishlists, or <c>null</c> to not constrain by monitoring.
    /// </summary>
    public bool? Monitored { get; init; }
}
