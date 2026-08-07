using System;

namespace Magpie.Models.Data;

/// <summary>
/// A content rating from a ratings body.
/// </summary>
public record ContentRating
{
    /// <summary>
    /// The unique id of the content rating.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the metadata contribution that contains this rating.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The position in the display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The name of the body that gives the rating.
    /// </summary>
    public required string Body { get; set; }

    /// <summary>
    /// The rating value.
    /// </summary>
    public required string Rating { get; set; }
}
