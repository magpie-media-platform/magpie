using System;

namespace Magpie.Models.Data;

/// <summary>
/// A genre classification.
/// </summary>
public record Genre
{
    /// <summary>
    /// The genre id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the metadata contribution this genre belongs to.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The genre name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The display order.
    /// </summary>
    public int Ordinal { get; set; }
}
