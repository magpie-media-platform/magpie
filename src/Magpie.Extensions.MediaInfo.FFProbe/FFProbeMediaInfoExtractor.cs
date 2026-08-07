using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Sdk.Media;

using Microsoft.Extensions.Logging;

using MediaInfoModel = Magpie.Models.Data.MediaInfo;

namespace Magpie.Extensions.MediaInfo.FFProbe;

/// <summary>
/// The FFprobe-backed media info extractor.
/// </summary>
public sealed class FFProbeMediaInfoExtractor(ILogger<FFProbeMediaInfoExtractor> logger) : IMediaInfoExtractor
{
    private readonly ILogger<FFProbeMediaInfoExtractor> _logger = logger;

    private static string StreamName(Dictionary<string, string>? tags, string fallback)
    {
        if (tags != null && tags.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        return fallback;
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? DetermineVideoRange(string? colorTransfer)
    {
        if (string.IsNullOrWhiteSpace(colorTransfer))
        {
            return null;
        }

        return colorTransfer switch
        {
            "smpte2084" or "arib-std-b67" => "HDR",
            _ => "SDR"
        };
    }

    private static bool? DispositionFlag(Dictionary<string, bool>? disposition, string key)
    {
        if (disposition is null)
        {
            return null;
        }

        return disposition.TryGetValue(key, out var value) ? value : null;
    }

    private static Dictionary<string, string> TagsToProperties(Dictionary<string, string>? tags)
    {
        if (tags == null || tags.Count == 0)
        {
            return [];
        }

        var properties = new Dictionary<string, string>(tags.Count);
        foreach (var kvp in tags)
        {
            if (!string.IsNullOrWhiteSpace(kvp.Value))
            {
                properties[kvp.Key] = kvp.Value;
            }
        }

        return properties;
    }

    /// <inheritdoc />
    public async Task<MediaInfoModel?> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("The FFprobe extractor skipped the missing file {FilePath}.", filePath);
            return null;
        }

        FFMpegCore.IMediaAnalysis analysis;
        try
        {
            analysis = await FFMpegCore.FFProbe
                .AnalyseAsync(filePath, null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "FFprobe failed to analyze the file {FilePath}.",
                filePath);
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var tracks = new List<MediaInfoTrack>();

        foreach (var stream in analysis.VideoStreams)
        {
            tracks.Add(new MediaInfoTrack
            {
                Type = "Video",
                Name = StreamName(stream.Tags, fallback: $"video:{stream.Index}"),
                Codec = NullIfEmpty(stream.CodecName),
                BitRate = stream.BitRate > 0 ? stream.BitRate : null,
                DurationSeconds = stream.Duration.TotalSeconds > 0 ? stream.Duration.TotalSeconds : null,
                Width = stream.Width > 0 ? stream.Width : null,
                Height = stream.Height > 0 ? stream.Height : null,
                FrameRate = stream.FrameRate > 0 ? stream.FrameRate : null,
                VideoRange = DetermineVideoRange(stream.ColorTransfer),
                BitDepth = stream.BitDepth,
                Language = NullIfEmpty(stream.Language),
                Forced = DispositionFlag(stream.Disposition, "forced"),
                Default = DispositionFlag(stream.Disposition, "default"),
                Sdh = DispositionFlag(stream.Disposition, "hearing_impaired"),
                Properties = TagsToProperties(stream.Tags)
            });
        }

        foreach (var stream in analysis.AudioStreams)
        {
            tracks.Add(new MediaInfoTrack
            {
                Type = "Audio",
                Name = StreamName(stream.Tags, fallback: $"audio:{stream.Index}"),
                Codec = NullIfEmpty(stream.CodecName),
                BitRate = stream.BitRate > 0 ? stream.BitRate : null,
                DurationSeconds = stream.Duration.TotalSeconds > 0 ? stream.Duration.TotalSeconds : null,
                Channels = stream.Channels > 0 ? stream.Channels : null,
                SampleRate = stream.SampleRateHz > 0 ? stream.SampleRateHz : null,
                BitDepth = stream.BitDepth,
                Language = NullIfEmpty(stream.Language),
                Forced = DispositionFlag(stream.Disposition, "forced"),
                Default = DispositionFlag(stream.Disposition, "default"),
                Sdh = DispositionFlag(stream.Disposition, "hearing_impaired"),
                Properties = TagsToProperties(stream.Tags)
            });
        }

        foreach (var stream in analysis.SubtitleStreams)
        {
            tracks.Add(new MediaInfoTrack
            {
                Type = "Subtitle",
                Name = StreamName(stream.Tags, fallback: $"subtitle:{stream.Index}"),
                Codec = NullIfEmpty(stream.CodecName),
                DurationSeconds = stream.Duration.TotalSeconds > 0 ? stream.Duration.TotalSeconds : null,
                Language = NullIfEmpty(stream.Language),
                Forced = DispositionFlag(stream.Disposition, "forced"),
                Default = DispositionFlag(stream.Disposition, "default"),
                Sdh = DispositionFlag(stream.Disposition, "hearing_impaired"),
                Properties = TagsToProperties(stream.Tags)
            });
        }

        var format = analysis.Format;
        return new MediaInfoModel
        {
            Container = NullIfEmpty(format?.FormatName),
            DurationSeconds = analysis.Duration.TotalSeconds > 0 ? analysis.Duration.TotalSeconds : null,
            BitRate = format is not null && format.BitRate > 0 ? (long)format.BitRate : null,
            Tracks = tracks
        };
    }
}
