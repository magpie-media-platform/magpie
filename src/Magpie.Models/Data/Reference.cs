using System;

namespace Magpie.Models.Data;

/// <summary>
/// An external reference or link.
/// </summary>
public record Reference
{
    /// <summary>
    /// The reference id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The metadata contribution this reference belongs to.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The kind of reference (e.g., "homepage", "social", "database").
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>
    /// Attribution source of the reference (e.g., "Wikipedia", "IMDB").
    /// </summary>
    public required string Source { get; set; }

    /// <summary>
    /// The URL of the reference.
    /// </summary>
    public required Uri Url { get; set; }

    /// <summary>
    /// The display name for the link.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// The language of the referenced content.
    /// </summary>
    public string? Language { get; set; }
}
