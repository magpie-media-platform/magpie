using System;

namespace Magpie.Models.Content;

/// <summary>
/// Technical metadata extracted from a media file.
/// </summary>
/// <param name="Container">The container format (mkv, mp4, avi, etc.).</param>
/// <param name="Duration">The duration of the media.</param>
/// <param name="VideoCodec">The video codec (H.264, HEVC, VP9, AV1, etc.).</param>
/// <param name="VideoWidth">The video width in pixels.</param>
/// <param name="VideoHeight">The video height in pixels.</param>
/// <param name="VideoBitrate">The video bitrate in bits per second.</param>
/// <param name="AudioCodec">The primary audio codec (AAC, AC3, DTS, FLAC, etc.).</param>
/// <param name="AudioChannels">The primary audio channel count.</param>
/// <param name="AudioBitrate">The primary audio bitrate in bits per second.</param>
public sealed record MediaInfo(
    string? Container,
    TimeSpan? Duration,
    string? VideoCodec,
    int? VideoWidth,
    int? VideoHeight,
    int? VideoBitrate,
    string? AudioCodec,
    int? AudioChannels,
    int? AudioBitrate);
