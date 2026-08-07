using System;

namespace Magpie.Models.Data;

/// <summary>
/// A genre classification.
/// </summary>
public record Genre
{
    /// <summary>
    /// The unique id of the genre.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the metadata contribution that contains this genre.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The display name of the genre.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The position in the display order.
    /// </summary>
    public int Ordinal { get; set; }
}
