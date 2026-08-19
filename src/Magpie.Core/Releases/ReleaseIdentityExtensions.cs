using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Models.Data;

namespace Magpie.Core.Releases;

internal static class ReleaseIdentityExtensions
{
    internal static bool RefersToSameRelease(this Release left, Release right)
    {
        var leftKeys = IdentityKeys(left);
        var rightKeys = IdentityKeys(right);
        var leftImmutableKeys = ImmutableKeys(leftKeys);
        var rightImmutableKeys = ImmutableKeys(rightKeys);
        if (leftImmutableKeys.Count > 0 && rightImmutableKeys.Count > 0)
        {
            return leftImmutableKeys.Overlaps(rightImmutableKeys);
        }

        return leftKeys.Overlaps(rightKeys) || SourceIdentities(left).Overlaps(SourceIdentities(right));
    }

    internal static bool HasConflictingImmutableIdentity(this Release left, Release right)
    {
        var leftKeys = ImmutableKeys(IdentityKeys(left));
        var rightKeys = ImmutableKeys(IdentityKeys(right));
        return leftKeys.Count > 0 && rightKeys.Count > 0 && !leftKeys.Overlaps(rightKeys);
    }

    private static HashSet<(string Authority, string Value)> IdentityKeys(Release release) =>
        release.IdentityKeys
            .Where(key => !string.IsNullOrWhiteSpace(key.Authority) && !string.IsNullOrWhiteSpace(key.Value))
            .Select(key => (
                key.Authority.Trim().ToLowerInvariant(),
                key.Value.Trim().ToLowerInvariant()))
            .ToHashSet();

    private static HashSet<(string Authority, string Value)> ImmutableKeys(
        HashSet<(string Authority, string Value)> keys) =>
        keys.Where(key => key.Authority != "fallback" &&
            !key.Authority.StartsWith("indexer:", StringComparison.Ordinal)).ToHashSet();

    private static HashSet<(Guid IndexerId, string? ExternalId, Uri? InfoUrl)> SourceIdentities(Release release)
    {
        var identities = new HashSet<(Guid IndexerId, string? ExternalId, Uri? InfoUrl)>();
        foreach (var source in release.Sources)
        {
            AddSourceIdentity(identities, source.IndexerId, source.ExternalId, source.InfoUrl);
        }

        AddSourceIdentity(identities, release.IndexerId, release.ExternalId, release.InfoUrl);
        return identities;
    }

    private static void AddSourceIdentity(
        HashSet<(Guid IndexerId, string? ExternalId, Uri? InfoUrl)> identities,
        Guid? indexerId,
        string? externalId,
        Uri? infoUrl)
    {
        if (indexerId is not { } value)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(externalId))
        {
            identities.Add((value, externalId, null));
        }
        else if (infoUrl is not null)
        {
            identities.Add((value, null, infoUrl));
        }
    }
}
