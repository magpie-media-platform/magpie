using System;
using System.Globalization;

namespace Magpie.Extensions.MusicBrainz;

internal static class CoverArtArchiveTargetExtensions
{
    internal static string ReleaseGroupFrontTarget(this string releaseGroupMbid, int thumbnailSize)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "release-group/{0}/front-{1}",
            Uri.EscapeDataString(releaseGroupMbid),
            thumbnailSize);
    }
}
