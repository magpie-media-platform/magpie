using System;

namespace Magpie.Models.Data;

/// <summary>
/// A trailer or other preview clip for content.
/// </summary>
public record Preview
{
    /// <summary>
    /// The unique id of the preview.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the contribution that supplied the preview.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The position in display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The kind of preview, such as trailer or clip.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>
    /// The MIME type of the preview.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// The slug of the strategy that embeds the preview in a page.
    /// </summary>
    public string? EmbedKind { get; set; }

    /// <summary>
    /// The URL of the preview.
    /// </summary>
    public required Uri Url { get; set; }

    /// <summary>
    /// The name of the upstream source.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// The title of the preview to show.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The date when the preview became public.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// The language of the preview, as a language code.
    /// </summary>
    public string? Language { get; set; }
}
