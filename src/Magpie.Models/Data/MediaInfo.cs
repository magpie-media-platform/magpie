using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// Information about media file contents.
/// </summary>
public record MediaInfo
{
    /// <summary>
    /// The tracks contained in the media.
    /// </summary>
    public List<MediaInfoTrack> Tracks { get; init; } = [];
}

