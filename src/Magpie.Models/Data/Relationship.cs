using System;

namespace Magpie.Models.Data;

/// <summary>
/// A link from a metadata contribution to another metadata container.
/// </summary>
public record Relationship
{
    /// <summary>
    /// The unique id of the relationship.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the contribution that supplied the link.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The id of the container at the other end of the link.
    /// </summary>
    public Guid? RelatedMetadataContainerId { get; set; }

    /// <summary>
    /// The position in display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The kind of link, as a provider-defined slug.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>
    /// The extra detail for the kind.
    /// </summary>
    public string? Qualifier { get; set; }

    /// <summary>
    /// The <see cref="MetadataContainer"/> at the other end of the link.
    /// </summary>
    public MetadataContainer? RelatedMetadataContainer { get; set; }
}
