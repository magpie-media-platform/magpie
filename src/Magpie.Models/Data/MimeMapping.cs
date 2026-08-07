using System;

namespace Magpie.Models.Data;

/// <summary>
/// A map from one file extension to one MIME type.
/// </summary>
public sealed record MimeMapping
{
    /// <summary>
    /// The file extension that the map matches.
    /// </summary>
    public required string Extension { get; set; }

    /// <summary>
    /// The MIME type for the file extension.
    /// </summary>
    public required string MimeType { get; set; }

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The UTC time when the record last changed.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
