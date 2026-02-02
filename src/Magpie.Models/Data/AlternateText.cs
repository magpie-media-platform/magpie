using System;

namespace Magpie.Models.Data;

/// <summary>
/// An alternate title or name for content.
/// </summary>
public record AlternateText
{
    /// <summary>
    /// The alternate text id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The metadata contribution this alternate text belongs to.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The kind of alternate text (i.e. "Title", "Description").
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>
    /// The language of this alternate text.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// The alternate text value.
    /// </summary>
    public required string Text { get; set; }
}
