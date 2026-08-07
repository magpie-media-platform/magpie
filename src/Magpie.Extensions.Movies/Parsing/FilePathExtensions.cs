using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

using Magpie.Sdk.Content;

namespace Magpie.Extensions.Movies.Parsing;

internal static class FilePathExtensions
{
    public static ParsedMovieFile ParseMovieFilename(this string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath) ?? string.Empty;

        var stemNormalised = stem.NormalizeMediaFilename();
        var yearMatch = stemNormalised.FindYearTokens();
        var source = stemNormalised;
        var lastYearMatch = LastOrDefault(yearMatch);

        if (lastYearMatch is null)
        {
            var parent = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? string.Empty;
            if (!string.IsNullOrEmpty(parent))
            {
                var parentNormalised = parent.NormalizeMediaFilename();
                var parentMatches = parentNormalised.FindYearTokens();
                var parentLast = LastOrDefault(parentMatches);
                if (parentLast is not null)
                {
                    source = parentNormalised;
                    lastYearMatch = parentLast;
                }
            }
        }

        if (lastYearMatch is null)
        {
            return new ParsedMovieFile
            {
                Title = CleanTitle(stemNormalised, releaseGroup: null),
                Year = null
            };
        }

        var year = int.Parse(lastYearMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var titleSegment = source.Substring(0, lastYearMatch.Index).Trim();
        var tagSegment = source.Substring(lastYearMatch.Index + lastYearMatch.Length).Trim();

        var group = ExtractReleaseGroup(tagSegment);
        var title = CleanTitle(titleSegment, group);

        return new ParsedMovieFile
        {
            Title = title,
            Year = year
        };
    }

    private static string? ExtractReleaseGroup(string tagSegment)
    {
        if (string.IsNullOrWhiteSpace(tagSegment))
        {
            return null;
        }

        var dashIndex = tagSegment.LastIndexOf('-');
        if (dashIndex < 0 || dashIndex == tagSegment.Length - 1)
        {
            return null;
        }

        var candidate = tagSegment.Substring(dashIndex + 1).Trim();
        if (candidate.Length is 0 or > 32)
        {
            return null;
        }

        foreach (var c in candidate)
        {
            if (!char.IsLetterOrDigit(c) && c != '.')
            {
                return null;
            }
        }

        return candidate;
    }

    private static string CleanTitle(string raw, string? releaseGroup)
    {
        var trimmed = raw.Trim().Trim('-').Trim();
        if (!string.IsNullOrEmpty(releaseGroup) &&
            trimmed.EndsWith("-" + releaseGroup, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - releaseGroup.Length - 1).Trim();
        }

        return trimmed;
    }

    private static Match? LastOrDefault(MatchCollection matches)
    {
        if (matches.Count == 0)
        {
            return null;
        }

        return matches[matches.Count - 1];
    }
}
