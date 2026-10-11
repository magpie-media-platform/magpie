using System;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Models.Data;

/// <summary>
/// A discovered wishlist entry with the identity and status of its content item.
/// </summary>
public sealed record WishlistDiscoveredEnrichedRow
{
    /// <summary>
    /// The id of the discovered entry.
    /// </summary>
    public required Guid DiscoveredId { get; init; }

    /// <summary>
    /// The name of the provider that issued the external id.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// The id of the candidate at the provider.
    /// </summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// The title of the candidate to show.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The UTC time when the refresh first found the candidate.
    /// </summary>
    public required DateTime DiscoveredAt { get; init; }

    /// <summary>
    /// The id of the content item that the entry links to.
    /// </summary>
    public Guid? LinkId { get; init; }

    /// <summary>
    /// The URL of the poster image.
    /// </summary>
    public string? PosterUrl { get; init; }

    /// <summary>
    /// The date of first publication.
    /// </summary>
    public DateTime? PublishDate { get; init; }

    /// <summary>
    /// The calendar precision of <see cref="PublishDate"/>.
    /// </summary>
    public TemporalPrecision? PublishDatePrecision { get; init; }

    /// <summary>
    /// The UTC time when the library received the content item.
    /// </summary>
    public DateTime? AddedAt { get; init; }

    /// <summary>
    /// The library state of the item, from <see cref="WishlistDiscoveredItemStatus"/>.
    /// </summary>
    public WishlistDiscoveredItemStatus? Status { get; init; }
}
