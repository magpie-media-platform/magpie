using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Sdk.Media;

using MediaInfoModel = Magpie.Models.Data.MediaInfo;

namespace Magpie.Extensions.IntegrationTestShims.MediaInfo;

internal sealed class MockMediaInfoExtractor : IMediaInfoExtractor
{
    public Task<MediaInfoModel?> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return Task.FromResult<MediaInfoModel?>(null);
        }

        var mediaInfo = Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".flac" => new MediaInfoModel
            {
                Container = "flac",
                DurationSeconds = 1,
                Tracks =
                [
                    new MediaInfoTrack
                    {
                        Type = "Audio",
                        Name = "audio:0",
                        Codec = "flac",
                        DurationSeconds = 1,
                        Channels = 2,
                        SampleRate = 44100
                    }
                ]
            },
            ".mkv" => new MediaInfoModel
            {
                Container = "matroska,webm",
                DurationSeconds = 1,
                Tracks =
                [
                    new MediaInfoTrack
                    {
                        Type = "Video",
                        Name = "video:0",
                        Codec = "h264",
                        DurationSeconds = 1,
                        Width = 320,
                        Height = 180,
                        FrameRate = 24,
                        VideoRange = "SDR"
                    },
                    new MediaInfoTrack
                    {
                        Type = "Audio",
                        Name = "audio:1",
                        Codec = "aac",
                        DurationSeconds = 1,
                        Channels = 2,
                        SampleRate = 44100
                    }
                ]
            },
            _ => null
        };

        return Task.FromResult(mediaInfo);
    }
}
