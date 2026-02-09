using System;

namespace Magpie.Models.Data;

/// <summary>
/// A physical media file associated with a content item.
/// </summary>
public record MediaFile
{
    /// <summary>
    /// The media file id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The content item this file belongs to.
    /// </summary>
    public Guid ContentItemId { get; set; }

    /// <summary>
    /// The kind of media file (Video, Audio, Subtitle, etc.).
    /// </summary>
    public required string FileKind { get; set; }

    /// <summary>
    /// The MIME type of the file.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// The file path.
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// The file size in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Media information about the file contents.
    /// </summary>
    /// <remarks>
    /// The media info is stored as serialized JSON.
    /// </remarks>
    public MediaInfo? MediaInfo { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user that created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// When the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user that last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
