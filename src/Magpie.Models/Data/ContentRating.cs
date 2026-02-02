using System;

namespace Magpie.Models.Data;

/// <summary>
/// A content rating from a ratings body.
/// </summary>
public record ContentRating
{
    /// <summary>
    /// The content rating id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the metadata contribution this rating belongs to.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The organization that issued the rating.
    /// </summary>
    public required string RatingsBody { get; set; }

    /// <summary>
    /// The rating value.
    /// </summary>
    public required string Value { get; set; }
}
