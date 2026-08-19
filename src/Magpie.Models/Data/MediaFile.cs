using System;
using System.Text.Json.Serialization;

namespace Magpie.Models.Data;

/// <summary>
/// A file on disk that belongs to a content item.
/// </summary>
public record MediaFile
{
    private string? _pathKey;

    /// <summary>
    /// The unique id of the media file.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the content item that owns the file.
    /// </summary>
    public Guid ContentItemId { get; set; }

    /// <summary>
    /// The role of the file, such as video, audio, or subtitle.
    /// </summary>
    public required string FileKind { get; set; }

    /// <summary>
    /// The MIME type of the file contents.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// The full path of the file on disk.
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// The platform-specific key used to find the host path.
    /// </summary>
    [JsonIgnore]
    public string PathKey
    {
        get => _pathKey ?? FilePath;
        set => _pathKey = value;
    }

    /// <summary>
    /// The size of the file in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// The technical properties of the file contents.
    /// </summary>
    public MediaInfo? MediaInfo { get; set; }

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time when the record last changed.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last changed the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
