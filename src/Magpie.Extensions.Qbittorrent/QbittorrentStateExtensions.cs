using Magpie.Models.Enums;

namespace Magpie.Extensions.Qbittorrent;

internal static class QbittorrentStateExtensions
{
    private const int GlobalShareLimit = -2;

    internal static MappingResult ToMappingResult(this string? state)
    {
        if (string.IsNullOrEmpty(state))
        {
            return new MappingResult(DownloadStatus.Grabbed, "qBittorrent returned no state");
        }

        return state switch
        {
            "error" => new MappingResult(DownloadStatus.Failed, "qBittorrent state: error"),
            "missingFiles" => new MappingResult(
                DownloadStatus.Failed, "qBittorrent state: missingFiles (data files not found)"),
            "pausedUP" or "stoppedUP"
                => new MappingResult(DownloadStatus.Downloaded, StatusReason: null, Paused: true),
            "uploading" or "seeding" or "queuedUP" or "stalledUP" or "checkingUP" or "forcedUP"
                => new MappingResult(DownloadStatus.Downloaded, StatusReason: null),
            "downloading" or "forcedDL" or "metaDL" or "forcedMetaDL"
                => new MappingResult(DownloadStatus.Downloading, StatusReason: null),
            "pausedDL" or "stoppedDL" => new MappingResult(DownloadStatus.Paused, StatusReason: null),
            "queuedDL" => new MappingResult(DownloadStatus.Queued, StatusReason: null),
            "stalledDL" => new MappingResult(
                DownloadStatus.Stalled,
                "qBittorrent state: stalledDL (no connected seeds/peers)"),
            "checkingDL" or "checkingResumeData" or "allocating" or "moving"
                => new MappingResult(DownloadStatus.Downloading, StatusReason: null),
            "unknown" => new MappingResult(DownloadStatus.Stalled, "qBittorrent state: unknown"),
            _ => new MappingResult(DownloadStatus.Grabbed, $"qBittorrent state: {state}")
        };
    }

    internal static bool IsHeldByPolicy(
        this MappingResult mapping,
        double ratio,
        double maxRatio,
        long seedingTimeSeconds,
        long maxSeedingTime)
    {
        if (mapping.Status is not DownloadStatus.Downloaded)
        {
            return false;
        }

        var ratioReached = maxRatio >= 0 && ratio >= maxRatio;
        var seedingTimeReached = maxSeedingTime >= 0 && seedingTimeSeconds / 60.0 >= maxSeedingTime;
        if (ratioReached || seedingTimeReached)
        {
            return false;
        }

        var followsGlobalLimit = maxRatio <= GlobalShareLimit || maxSeedingTime <= GlobalShareLimit;
        return !followsGlobalLimit || !mapping.Paused;
    }
}
