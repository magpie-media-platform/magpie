using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Core.Releases;

internal sealed class ReleaseBlockList(IDownloadRepository downloadRepository)
{
    public static HashSet<Guid> Blocked(
        IReadOnlyDictionary<Guid, (DownloadStatus Status, DownloadFailureReason? FailureReason)> statuses) =>
        [
            .. statuses
                .Where(status => status.Value.Status is DownloadStatus.Failed or DownloadStatus.Imported)
                .Select(status => status.Key)
        ];

    public static (DownloadStatus? AcquisitionStatus, DownloadFailureReason? FailureReason) ResolveAcquisition(
        IReadOnlyDictionary<Guid, Download> downloadsByReleaseId,
        IReadOnlyDictionary<(Guid IndexerId, string ExternalId), Download> downloadsByIndexerExternalId,
        Release release)
    {
        if (!downloadsByReleaseId.TryGetValue(release.Id, out var download) &&
            (release.IndexerId is not { } indexerId ||
                string.IsNullOrWhiteSpace(release.ExternalId) ||
                !downloadsByIndexerExternalId.TryGetValue((indexerId, release.ExternalId), out download)))
        {
            return (null, null);
        }

        var failureReason = DownloadStatuses.Failures.Contains(download.Status)
            ? download.FailureReason
            : null;
        return (download.Status, failureReason);
    }

    public static Dictionary<(Guid IndexerId, string ExternalId), Download> BuildDownloadsByIndexerExternalId(
        IReadOnlyList<Release> releases,
        IReadOnlyDictionary<Guid, Download> downloadsByReleaseId)
    {
        var keyByReleaseId = new Dictionary<Guid, (Guid IndexerId, string ExternalId)>();
        foreach (var release in releases)
        {
            if (release.IndexerId is { } indexerId && !string.IsNullOrWhiteSpace(release.ExternalId))
            {
                keyByReleaseId[release.Id] = (indexerId, release.ExternalId);
            }
        }

        var downloadsByKey = new Dictionary<(Guid IndexerId, string ExternalId), Download>();
        foreach (var download in downloadsByReleaseId.Values)
        {
            if (download.ReleaseId is not { } downloadReleaseId ||
                !keyByReleaseId.TryGetValue(downloadReleaseId, out var key))
            {
                continue;
            }

            if (!downloadsByKey.TryGetValue(key, out var existing) || download.CreatedAt > existing.CreatedAt)
            {
                downloadsByKey[key] = download;
            }
        }

        return downloadsByKey;
    }

    public async Task<Dictionary<Guid, (DownloadStatus Status, DownloadFailureReason? FailureReason)>>
        ResolveStatusesAsync(
        IReadOnlyList<Release> releases,
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken)
    {
        var downloads = await downloadRepository.ListByReleaseAsync(releases, cancellationToken).ConfigureAwait(false);

        var statuses = new Dictionary<Guid, (DownloadStatus Status, DownloadFailureReason? FailureReason)>();
        foreach (var release in releases)
        {
            var releaseKeys = NormalizedKeys(release.IdentityKeys);
            var releaseDownloads = downloads.Where(download => IsDownloadOf(download, release, releaseKeys)).ToList();
            if (releaseDownloads.Any(download => download.Status == DownloadStatus.Imported &&
                (download.Scope.Count == 0 ||
                    download.Scope.Any(scope => scope.ContentHierarchyItemId == contentHierarchyItemId))))
            {
                statuses[release.Id] = (DownloadStatus.Imported, null);
            }
            else if (LatestDownload(releaseDownloads, DownloadStatus.Failed) is { } failed)
            {
                statuses[release.Id] = (DownloadStatus.Failed, failed.FailureReason);
            }
            else if (LatestDownload(releaseDownloads, DownloadStatus.Faulted) is { } faulted)
            {
                statuses[release.Id] = (DownloadStatus.Faulted, faulted.FailureReason);
            }
        }

        return statuses;
    }

    public async Task<Dictionary<Guid, Download>> LoadDownloadsByReleaseIdAsync(
        IReadOnlyList<Guid> contentHierarchyItemIds,
        IReadOnlyList<Release> releases,
        CancellationToken cancellationToken)
    {
        var downloads = new List<Download>();
        if (contentHierarchyItemIds.Count > 0)
        {
            downloads.AddRange(await downloadRepository.QueryAllAsync(
                new DownloadQuery { ContentHierarchyItemIds = contentHierarchyItemIds },
                new()
                {
                    PropertyName = nameof(Download.CreatedAt),
                    Direction = SortDirection.Descending
                },
                cancellationToken).ConfigureAwait(false));
        }

        var downloadsByReleaseId = LatestDownloadsByReleaseId(downloads);

        var missingReleaseIds = releases
            .Where(release => !downloadsByReleaseId.ContainsKey(release.Id))
            .Select(release => release.Id)
            .Distinct()
            .ToList();
        if (missingReleaseIds.Count > 0)
        {
            var missingDownloads = await downloadRepository.QueryAllAsync(
                new DownloadQuery { ReleaseIds = missingReleaseIds },
                new()
                {
                    PropertyName = nameof(Download.CreatedAt),
                    Direction = SortDirection.Descending
                },
                cancellationToken).ConfigureAwait(false);

            var latestByReleaseId = LatestDownloadsByReleaseId(missingDownloads);
            foreach (var releaseId in missingReleaseIds)
            {
                if (latestByReleaseId.TryGetValue(releaseId, out var download) && IsTerminalDownload(download))
                {
                    downloads.Add(download);
                }
            }
        }

        return LatestDownloadsByReleaseId(downloads);
    }

    private static bool IsTerminalDownload(Download download) =>
        download.Status is DownloadStatus.Failed or DownloadStatus.Faulted or DownloadStatus.Cancelled;

    private static Dictionary<Guid, Download> LatestDownloadsByReleaseId(IEnumerable<Download> downloads)
    {
        return downloads
            .Where(d => d.ReleaseId is not null)
            .GroupBy(d => d.ReleaseId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Id).First());
    }

    private static bool IsDownloadOf(
        Download download,
        Release release,
        HashSet<(string Authority, string Value)> releaseKeys) =>
        download.ReleaseId == release.Id ||
        (release.IndexerId is not null &&
            !string.IsNullOrWhiteSpace(release.ExternalId) &&
            download.Release?.IndexerId == release.IndexerId &&
            download.Release.ExternalId == release.ExternalId) ||
        NormalizedKeys(download.ReleaseIdentityKeys).Overlaps(releaseKeys);

    private static Download? LatestDownload(IEnumerable<Download> downloads, DownloadStatus status) =>
        downloads
            .Where(download => download.Status == status)
            .OrderByDescending(download => download.FailedAt ?? download.ModifiedAt)
            .FirstOrDefault();

    private static HashSet<(string Authority, string Value)> NormalizedKeys(IEnumerable<ReleaseIdentityKey> keys) =>
        [
            .. keys
                .Where(key => !string.IsNullOrWhiteSpace(key.Authority) && !string.IsNullOrWhiteSpace(key.Value))
                .Select(key => (key.Authority.Trim(), key.Value.Trim()))
        ];
}
