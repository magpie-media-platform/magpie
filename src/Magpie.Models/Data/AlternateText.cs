using System;

namespace Magpie.Models.Data;

/// <summary>
/// An alternate title or name for content.
/// </summary>
public record AlternateText
{
    /// <summary>
    /// The unique id of the alternate text.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the metadata contribution that contains this text.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The position in the display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The kind of text, for example a title or a description.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>
    /// The language or locale code of the text.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// The alternate text value.
    /// </summary>
    public required string Text { get; set; }
}
