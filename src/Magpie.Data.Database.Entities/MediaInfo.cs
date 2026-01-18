using System;

namespace Magpie.Data.Database.Entities;

public class MediaInfo
{
    public string? Container { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? VideoCodec { get; set; }
    public int? VideoWidth { get; set; }
    public int? VideoHeight { get; set; }
    public int? VideoBitrate { get; set; }
    public string? AudioCodec { get; set; }
    public int? AudioChannels { get; set; }
    public int? AudioBitrate { get; set; }
}
