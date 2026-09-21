using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;

using Magpie.Models.Data;

namespace Magpie.Core.Releases;

internal sealed class ReleaseBlockList(IDownloadRepository downloadRepository, TimeProvider timeProvider)
{
    private static readonly TimeSpan LeafImportSuppressionWindow = TimeSpan.FromHours(12);

    public async Task<HashSet<Guid>> ResolveAsync(
        IReadOnlyList<Release> releases,
        Guid leafContentHierarchyItemId,
        CancellationToken cancellationToken)
    {
        var taints = await downloadRepository
            .ListFailedByReleaseIdentitiesAsync(
                [.. releases.SelectMany(release => release.IdentityKeys)],
                null,
                cancellationToken)
            .ConfigureAwait(false);
        var suppressions = await downloadRepository
            .ListImportedByContentHierarchyItemAsync(
                leafContentHierarchyItemId,
                timeProvider.GetUtcNow().UtcDateTime - LeafImportSuppressionWindow,
                cancellationToken)
            .ConfigureAwait(false);

        var blockingKeys = NormalizedKeys(
            taints.Concat(suppressions).SelectMany(download => download.ReleaseIdentityKeys));

        return
        [
            .. releases
                .Where(release => NormalizedKeys(release.IdentityKeys).Overlaps(blockingKeys))
                .Select(release => release.Id)
        ];
    }

    private static HashSet<(string Authority, string Value)> NormalizedKeys(
        IEnumerable<ReleaseIdentityKey> keys) =>
        [
            .. keys
                .Where(key => !string.IsNullOrWhiteSpace(key.Authority) && !string.IsNullOrWhiteSpace(key.Value))
                .Select(key => (key.Authority.Trim(), key.Value.Trim()))
        ];
}
