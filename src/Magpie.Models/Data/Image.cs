using System;

namespace Magpie.Models.Data;

/// <summary>
/// An image associated with the metadata.
/// </summary>
public record Image
{
    /// <summary>
    /// The image id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The metadata contribution this image belongs to.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The kind of image (e.g., "poster", "backdrop", "logo").
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>
    /// The MIME type of the image.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// The URL to the image.
    /// </summary>
    public required Uri Url { get; set; }

    /// <summary>
    /// Attribution source for the image.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// The language of text in the image, if applicable.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// The image width in pixels.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// The image height in pixels.
    /// </summary>
    public int? Height { get; set; }
}
