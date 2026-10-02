using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

using Magpie.Sdk.Content;

namespace Magpie.Extensions.Movies.Parsing;

internal static class FilePathExtensions
{
    private static readonly HashSet<string> ReleaseTagTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "480p",
        "540p",
        "576p",
        "720p",
        "1080p",
        "1080i",
        "2160p",
        "4320p",
        "4k",
        "8k",
        "uhd",
        "hd",
        "sd",
        "bluray",
        "bdrip",
        "brrip",
        "bdremux",
        "remux",
        "web-dl",
        "webdl",
        "webrip",
        "hdrip",
        "dvdrip",
        "dvd",
        "hdtv",
        "pdtv",
        "sdtv",
        "hdcam",
        "hdts",
        "telesync",
        "scr",
        "x264",
        "x265",
        "h264",
        "h265",
        "hevc",
        "avc",
        "av1",
        "xvid",
        "divx",
        "vp9",
        "10bit",
        "8bit",
        "hdr",
        "hdr10",
        "dolbyvision",
        "sdr",
        "aac",
        "ac3",
        "eac3",
        "dts",
        "dtshd",
        "truehd",
        "atmos",
        "flac",
        "mp3",
        "ddp",
        "ddp5",
        "dd5",
        "repack",
        "rerip",
        "imax",
        "vostfr"
    };

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

        return StripTrailingReleaseTags(trimmed);
    }

    private static string StripTrailingReleaseTags(string title)
    {
        var tokens = title.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        var keepLength = tokens.Length;
        while (keepLength > 1)
        {
            if (ReleaseTagTokens.Contains(tokens[keepLength - 1]))
            {
                keepLength--;
            }
            else if (keepLength > 2 && tokens[keepLength - 1] == "1" &&
                (string.Equals(tokens[keepLength - 2], "ddp5", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(tokens[keepLength - 2], "dd5", StringComparison.OrdinalIgnoreCase) ||
                 (keepLength > 3 && tokens[keepLength - 2] is "5" or "7" &&
                  ReleaseTagTokens.Contains(tokens[keepLength - 3]))))
            {
                keepLength -= 2;
            }
            else
            {
                break;
            }
        }

        return keepLength == tokens.Length
            ? title
            : string.Join(' ', tokens, 0, keepLength).Trim().TrimEnd('-').Trim();
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
