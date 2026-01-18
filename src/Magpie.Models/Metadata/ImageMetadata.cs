using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// An image for content.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Ordinal">The ordering within the list.</param>
/// <param name="ImageType">The image type.</param>
/// <param name="MimeType">The MIME type of the image.</param>
/// <param name="Url">The image URL.</param>
/// <param name="Attribution">The attribution for the image.</param>
/// <param name="Language">The language code, if applicable.</param>
/// <param name="Width">The image width in pixels.</param>
/// <param name="Height">The image height in pixels.</param>
public sealed record ImageMetadata(
    Guid Id,
    int Ordinal,
    string ImageType,
    string? MimeType,
    Uri Url,
    string? Attribution,
    string? Language,
    int? Width,
    int? Height);
