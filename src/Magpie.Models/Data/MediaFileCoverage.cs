using System;

namespace Magpie.Models.Data;

/// <summary>
/// A content item, other than the owner, that a media file also covers.
/// </summary>
public record MediaFileCoverage
{
    /// <summary>
    /// The id of the media file.
    /// </summary>
    public Guid MediaFileId { get; set; }

    /// <summary>
    /// The id of the covered content item.
    /// </summary>
    public Guid ContentItemId { get; set; }
}
