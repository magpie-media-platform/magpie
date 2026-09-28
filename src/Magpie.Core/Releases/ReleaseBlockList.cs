using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;

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

    public async Task<Dictionary<Guid, (DownloadStatus Status, DownloadFailureReason? FailureReason)>>
        ResolveStatusesAsync(
        IReadOnlyList<Release> releases,
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken)
    {
        var keys = releases.SelectMany(release => release.IdentityKeys).ToList();
        var downloads = await downloadRepository.ListByReleaseAsync(keys, cancellationToken).ConfigureAwait(false);
        var failed = downloads.Where(download => download.Status == DownloadStatus.Failed).ToList();
        var faulted = downloads.Where(download => download.Status == DownloadStatus.Faulted).ToList();

        var imports = downloads.Where(download => download.Status == DownloadStatus.Imported &&
            (download.Scope.Count == 0 ||
                download.Scope.Any(scope => scope.ContentHierarchyItemId == contentHierarchyItemId)));

        var failedKeys = NormalizedKeys(failed.SelectMany(download => download.ReleaseIdentityKeys));
        var faultedKeys = NormalizedKeys(faulted.SelectMany(download => download.ReleaseIdentityKeys));
        var importedKeys = NormalizedKeys(imports.SelectMany(download => download.ReleaseIdentityKeys));

        var statuses = new Dictionary<Guid, (DownloadStatus Status, DownloadFailureReason? FailureReason)>();
        foreach (var release in releases)
        {
            var releaseKeys = NormalizedKeys(release.IdentityKeys);
            if (releaseKeys.Overlaps(importedKeys))
            {
                statuses[release.Id] = (DownloadStatus.Imported, null);
            }
            else if (releaseKeys.Overlaps(failedKeys))
            {
                statuses[release.Id] = (DownloadStatus.Failed, LatestFailureReason(failed, releaseKeys));
            }
            else if (releaseKeys.Overlaps(faultedKeys))
            {
                statuses[release.Id] = (DownloadStatus.Faulted, LatestFailureReason(faulted, releaseKeys));
            }
        }

        return statuses;
    }

    private static DownloadFailureReason? LatestFailureReason(
        IEnumerable<Download> downloads,
        HashSet<(string Authority, string Value)> releaseKeys) =>
        downloads
            .Where(download => NormalizedKeys(download.ReleaseIdentityKeys).Overlaps(releaseKeys))
            .OrderByDescending(download => download.FailedAt ?? download.ModifiedAt)
            .Select(download => download.FailureReason)
            .FirstOrDefault();

    private static HashSet<(string Authority, string Value)> NormalizedKeys(
        IEnumerable<ReleaseIdentityKey> keys) =>
        [
            .. keys
                .Where(key => !string.IsNullOrWhiteSpace(key.Authority) && !string.IsNullOrWhiteSpace(key.Value))
                .Select(key => (key.Authority.Trim(), key.Value.Trim()))
        ];
}
