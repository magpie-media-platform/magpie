using System;

namespace Magpie.Models.Data;

/// <summary>
/// An image associated with the metadata.
/// </summary>
public record Image
{
    /// <summary>
    /// The unique id of the image.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the metadata contribution that contains this image.
    /// </summary>
    public Guid MetadataContributionId { get; set; }

    /// <summary>
    /// The position in the display order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The type of the image.
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// The MIME type of the image.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// The URL of the image.
    /// </summary>
    public required Uri Url { get; set; }

    /// <summary>
    /// The name of the source that supplies the image.
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

    /// <summary>
    /// Whether the image is the primary image of the container.
    /// </summary>
    public bool IsPrimary { get; set; }
}
