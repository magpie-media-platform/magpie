using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// The technical properties of the contents of a media file.
/// </summary>
public record MediaInfo
{
    /// <summary>
    /// The container format, such as mkv or mp4.
    /// </summary>
    public string? Container { get; set; }

    /// <summary>
    /// The run time in seconds.
    /// </summary>
    public double? DurationSeconds { get; set; }

    /// <summary>
    /// The overall bit rate in bits per second.
    /// </summary>
    public long? BitRate { get; set; }

    /// <summary>
    /// The video, audio, and subtitle tracks in the file.
    /// </summary>
    public List<MediaInfoTrack> Tracks { get; set; } = [];
}
