using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// A preview for content.
/// </summary>
/// <remarks>
/// Previews are typically video content such as movie trailers, TV show teasers, or music videos.
/// </remarks>
/// <param name="Id">The unique identifier.</param>
/// <param name="Ordinal">The ordering within the list.</param>
/// <param name="PreviewType">The preview type (e.g., trailer, teaser, clip).</param>
/// <param name="MimeType">The MIME type of the preview.</param>
/// <param name="Url">The preview URL.</param>
/// <param name="Attribution">The attribution for the preview.</param>
/// <param name="Title">The preview title, if available.</param>
/// <param name="ReleaseDate">The release date of the preview, if available.</param>
/// <param name="Language">The language code, if applicable.</param>
public sealed record PreviewMetadata(
    Guid Id,
    int Ordinal,
    string PreviewType,
    string? MimeType,
    Uri Url,
    string? Attribution,
    string? Title,
    DateTime? ReleaseDate,
    string? Language);
