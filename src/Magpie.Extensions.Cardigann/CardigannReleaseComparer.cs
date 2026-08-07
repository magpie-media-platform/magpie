using System;
using System.Globalization;
using System.Text.Json;

using Magpie.Models.Data;
using Magpie.Sdk.Releases;

namespace Magpie.Extensions.Cardigann;

internal sealed class CardigannReleaseComparer : IReleaseComparer
{
    public bool CanCompare(Release release)
    {
        ArgumentNullException.ThrowIfNull(release);

        var peers = ParsePeerCounts(release);
        return peers.Seeders is not null && peers.Leechers is not null;
    }

    public int Compare(Release left, Release right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftPeers = ParsePeerCounts(left);
        var rightPeers = ParsePeerCounts(right);

        if (leftPeers.Seeders is not { } leftSeeders ||
            leftPeers.Leechers is not { } leftLeechers ||
            rightPeers.Seeders is not { } rightSeeders ||
            rightPeers.Leechers is not { } rightLeechers)
        {
            return 0;
        }

        var seedersComparison = rightSeeders.CompareTo(leftSeeders);
        return seedersComparison != 0 ? seedersComparison : rightLeechers.CompareTo(leftLeechers);
    }

    private (int? Seeders, int? Leechers) ParsePeerCounts(Release release)
    {
        if (string.IsNullOrWhiteSpace(release.Properties))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(release.Properties);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            return (ParsePeerCount(document.RootElement, "seeders"), ParsePeerCount(document.RootElement, "leechers"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private int? ParsePeerCount(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var value) && value >= 0 => value,
            JsonValueKind.String when int.TryParse(
                element.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value) && value >= 0 => value,
            _ => null,
        };
    }
}
