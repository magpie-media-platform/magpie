using System;

namespace Magpie.Models.Data;

/// <summary>
/// An external identifier from a metadata provider.
/// </summary>
public record ExternalId
{
    /// <summary>
    /// The external id id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The metadata contribution this external ID belongs to.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The external identifier value.
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// The attribution source for the external id.
    /// </summary>
    public string? Source { get; set; }
}
