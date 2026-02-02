using System;

namespace Magpie.Models.Data;

/// <summary>
/// A preview or trailer for content.
/// </summary>
public record Preview
{
    /// <summary>
    /// The preview id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The metadata contribution this preview belongs to.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The kind of preview (e.g., "trailer", "teaser", "clip").
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>
    /// The MIME type of the preview.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// The URL to the preview.
    /// </summary>
    public required Uri Url { get; set; }

    /// <summary>
    /// Attribution source for the preview.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// The title of the preview.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// When the preview was released.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// The language of the preview.
    /// </summary>
    public string? Language { get; set; }
}
