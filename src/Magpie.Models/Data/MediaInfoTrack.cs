using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A single track inside a media file.
/// </summary>
public record MediaInfoTrack
{
    /// <summary>
    /// The kind of track, such as video, audio, or subtitle.
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// The name of the track to show.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The codec of the track, such as h264 or aac.
    /// </summary>
    public string? Codec { get; set; }

    /// <summary>
    /// The bit rate of the track in bits per second.
    /// </summary>
    public long? BitRate { get; set; }

    /// <summary>
    /// The run time of the track in seconds.
    /// </summary>
    public double? DurationSeconds { get; set; }

    /// <summary>
    /// The frame width in pixels.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// The frame height in pixels.
    /// </summary>
    public int? Height { get; set; }

    /// <summary>
    /// The frame rate in frames per second.
    /// </summary>
    public double? FrameRate { get; set; }

    /// <summary>
    /// The dynamic range of the video, such as SDR or HDR10.
    /// </summary>
    public string? VideoRange { get; set; }

    /// <summary>
    /// The number of bits per sample.
    /// </summary>
    public int? BitDepth { get; set; }

    /// <summary>
    /// The number of audio channels.
    /// </summary>
    public int? Channels { get; set; }

    /// <summary>
    /// The audio sample rate in hertz.
    /// </summary>
    public int? SampleRate { get; set; }

    /// <summary>
    /// The language of the track, as a language code.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Whether the media file marks the track as forced.
    /// </summary>
    public bool? Forced { get; set; }

    /// <summary>
    /// Whether the media file marks the track as the default track.
    /// </summary>
    public bool? Default { get; set; }

    /// <summary>
    /// Whether the track is for deaf and hard-of-hearing viewers.
    /// </summary>
    public bool? Sdh { get; set; }

    /// <summary>
    /// The additional track properties, keyed by name.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = [];
}
