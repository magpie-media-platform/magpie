using System;

namespace Magpie.Models.Data;

/// <summary>
/// A candidate that a wishlist refresh already reported.
/// </summary>
public record WishlistDiscovered
{
    /// <summary>
    /// The unique id of the entry.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the wishlist that found the candidate.
    /// </summary>
    public Guid WishlistId { get; set; }

    /// <summary>
    /// The name of the provider that issued the external id.
    /// </summary>
    public required string ExternalIdSource { get; set; }

    /// <summary>
    /// The id of the candidate at the provider.
    /// </summary>
    public required string ExternalId { get; set; }

    /// <summary>
    /// The title of the candidate to show.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The UTC time when the refresh first found the candidate.
    /// </summary>
    public DateTime DiscoveredAt { get; set; }
}
