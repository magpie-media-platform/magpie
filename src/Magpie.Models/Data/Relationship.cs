using System;

namespace Magpie.Models.Data;

/// <summary>
/// A contextual link between two metadata items.
/// </summary>
public record Relationship
{
    /// <summary>
    /// The relationship id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the metadata contribution the relationship the belongs to.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The id of the related metadata container.
    /// </summary>
    public Guid? RelatedMetadataContainerId { get; set; }

    /// <summary>
    /// The display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The kind of relationship (e.g., "director", "actor", "studio").
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>
    /// A qualifier for the relationship (e.g., character name for actors).
    /// </summary>
    public string? Qualifier { get; set; }
}
