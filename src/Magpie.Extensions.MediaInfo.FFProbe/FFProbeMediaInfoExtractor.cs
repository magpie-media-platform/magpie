using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using FFMpegCore.Exceptions;

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
    private const string InvalidDataError = "Invalid data found when processing input";

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

    internal static int? BitDepthFromPixelFormat(string? pixelFormat)
    {
        var planes = pixelFormat?.LastIndexOf('p') ?? -1;
        if (planes < 0)
        {
            return null;
        }

        var depth = pixelFormat!.Substring(planes + 1);
        if (depth.EndsWith("le", StringComparison.Ordinal) || depth.EndsWith("be", StringComparison.Ordinal))
        {
            depth = depth.Substring(0, depth.Length - 2);
        }

        return depth.Length == 0 ? 8 :
            int.TryParse(depth, NumberStyles.None, CultureInfo.InvariantCulture, out var bits) ? bits : null;
    }

    internal static bool IsUnreadableFormat(string? errorOutput) =>
        errorOutput?.TrimEnd().EndsWith(InvalidDataError, StringComparison.Ordinal) == true;

    private static void RecordOutcome(Activity? activity, Stopwatch stopwatch, string outcome)
    {
        var tag = new KeyValuePair<string, object?>("outcome", outcome);
        Telemetry.Invocations.Add(1, tag);
        Telemetry.Duration.Record(stopwatch.Elapsed.TotalMilliseconds, tag);
        activity?.SetTag("magpie.mediainfo.outcome", outcome);
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
            Telemetry.Invocations.Add(1, new KeyValuePair<string, object?>("outcome", "missing_file"));
            _logger.LogWarning("The FFprobe extractor skipped the missing file {FilePath}.", filePath);
            return null;
        }

        using var activity = Telemetry.Source.StartActivity("ffprobe.analyse");
        var stopwatch = Stopwatch.StartNew();

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
        catch (FFMpegException ex) when (IsUnreadableFormat(ex.FFMpegErrorOutput))
        {
            RecordOutcome(activity, stopwatch, "unreadable_format");
            _logger.LogDebug(ex, "FFprobe cannot read the format of the file {FilePath}.", filePath);
            return null;
        }
        catch (Exception ex)
        {
            RecordOutcome(activity, stopwatch, "analysis_failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("error.type", ex.GetType().FullName);
            _logger.LogWarning(ex, "FFprobe failed to analyze the file {FilePath}.", filePath);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();

        RecordOutcome(activity, stopwatch, "success");

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
                BitDepth = stream.BitDepth ?? BitDepthFromPixelFormat(stream.PixelFormat),
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
