using System;
using System.Globalization;
using System.Text.RegularExpressions;

using Magpie.Models.Releases;

namespace Magpie.Core.Releases;

internal readonly record struct ParsedReleaseTitle
{
    private static readonly Regex ProperRegex = new(
        @"(?<![A-Za-z0-9])proper(?<count>\d+)?(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RepackRegex = new(
        @"(?<![A-Za-z0-9])(?:repack|rerip)(?<count>\d+)?(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex VersionRegex = new(
        @"(?<![A-Za-z])v(?<version>\d+)(?![A-Za-z])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex GroupRegex = new(
        @"-(?<group>[A-Za-z0-9]+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ParsedReleaseTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            Revision = new ReleaseRevision(1, false, false);
            Group = null;
            return;
        }

        var proper = ProperRegex.Match(title);
        var repack = RepackRegex.Match(title);
        var explicitVersion = VersionRegex.Match(title);
        var version = Math.Max(1, RevisionVersion(proper.Groups["count"].Value, proper.Success));
        version = Math.Max(version, RevisionVersion(repack.Groups["count"].Value, repack.Success));
        if (explicitVersion.Success && int.TryParse(
            explicitVersion.Groups["version"].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsedVersion))
        {
            version = Math.Max(version, parsedVersion);
        }

        Revision = new ReleaseRevision(version, proper.Success, repack.Success);
        var group = GroupRegex.Match(title);
        Group = group.Success ? group.Groups["group"].Value : null;
    }

    public string? Group { get; }

    public ReleaseRevision Revision { get; }

    private static int RevisionVersion(string value, bool matched) =>
        !matched
            ? 1
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var round) && round >= 1
                ? round + 1
                : 2;
}
