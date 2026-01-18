using System;

namespace Magpie.Models.Content;

/// <summary>
/// A physical media file associated with a content hierarchy item.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Kind">The kind of media file (Video, Audio, Subtitle, etc.).</param>
/// <param name="MimeType">The MIME type of the file.</param>
/// <param name="RelativePath">The path to the file relative to its parent content hierarchy item's path.</param>
/// <param name="Size">The file size in bytes.</param>
/// <param name="MediaInfo">The technical metadata extracted from the file.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record MediaFile(
    Guid Id,
    string Kind,
    string? MimeType,
    string RelativePath,
    long Size,
    MediaInfo? MediaInfo,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
