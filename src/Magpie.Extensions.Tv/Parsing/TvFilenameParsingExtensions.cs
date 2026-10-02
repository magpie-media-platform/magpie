using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

using Magpie.Sdk.Content;

namespace Magpie.Extensions.Tv.Parsing;

internal static class TvFilenameParsingExtensions
{
    private static readonly Regex AnimeAbsoluteToken = new(
        @"^\s*\[(?<group>[^\]]+)\]\s*(?<title>.+?)\s*-\s*(?<episode>\d{1,4})(?:\s|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DatedEpisodeToken = new(
        @"(?<![0-9])(?<year>(19|20)\d{2})[\-\.](?<month>0[1-9]|1[0-2])[\-\.](?<day>0[1-9]|[12]\d|3[01])(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NumericLeafToken = new(
        @"^(?<episode>\d{1,4})(?:[\s\-\.]|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SeasonFolderToken = new(
        @"^season[\s\.\-_]*0*(?<season>\d{1,3})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    internal static ParsedTvFile? ParseTvFilename(this string filePath, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            reason = TvParsingMessages.EmptyTitleReason;
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(filePath) ?? string.Empty;
        var parentFolder = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? string.Empty;
        var grandparentFolder =
            Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(filePath))) ?? string.Empty;

        var normalisedStem = stem.NormalizeMediaFilename();
        var normalisedParent = parentFolder.NormalizeMediaFilename();
        var normalisedGrandparent = grandparentFolder.NormalizeMediaFilename();

        if (AnimeAbsoluteToken.IsMatch(stem))
        {
            reason = TvParsingMessages.AnimeAbsoluteNumberingReason;
            return null;
        }

        if (DatedEpisodeToken.IsMatch(normalisedStem) || DatedEpisodeToken.IsMatch(normalisedParent))
        {
            reason = TvParsingMessages.DatedEpisodeReason;
            return null;
        }

        var sxxEyyMatch = TvParsingPatterns.SeasonEpisode.Match(normalisedStem);
        if (sxxEyyMatch.Success)
        {
            var season = int.Parse(sxxEyyMatch.Groups["season"].Value, CultureInfo.InvariantCulture);
            var title = TitleFromStemOrFolder(
                normalisedStem,
                sxxEyyMatch.Index,
                normalisedParent,
                normalisedGrandparent,
                isSeasonFolderParent: IsSeasonFolder(normalisedParent));
            return BuildResult(title, season, sxxEyyMatch.ParseEpisodes(), out reason);
        }

        var numXMatch = TvParsingPatterns.NumXNum.Match(normalisedStem);
        if (numXMatch.Success)
        {
            var season = int.Parse(numXMatch.Groups["season"].Value, CultureInfo.InvariantCulture);
            var title = TitleFromStemOrFolder(
                normalisedStem,
                numXMatch.Index,
                normalisedParent,
                normalisedGrandparent,
                isSeasonFolderParent: IsSeasonFolder(normalisedParent));
            return BuildResult(title, season, numXMatch.ParseEpisodes(), out reason);
        }

        var seasonFolderMatch = SeasonFolderToken.Match(normalisedParent);
        if (seasonFolderMatch.Success)
        {
            var leafMatch = NumericLeafToken.Match(normalisedStem);
            if (leafMatch.Success)
            {
                var season = int.Parse(seasonFolderMatch.Groups["season"].Value, CultureInfo.InvariantCulture);
                var episode = int.Parse(leafMatch.Groups["episode"].Value, CultureInfo.InvariantCulture);
                return BuildResult(normalisedGrandparent, season, [episode], out reason);
            }
        }

        if (string.Equals(parentFolder, "Specials", StringComparison.OrdinalIgnoreCase))
        {
            var leafMatch = NumericLeafToken.Match(normalisedStem);
            if (leafMatch.Success)
            {
                var episode = int.Parse(leafMatch.Groups["episode"].Value, CultureInfo.InvariantCulture);
                return BuildResult(normalisedGrandparent, season: 0, [episode], out reason);
            }
        }

        reason = TvParsingMessages.NoSeasonEpisodeReason;
        return null;
    }

    private static ParsedTvFile? BuildResult(
        string title,
        int season,
        IReadOnlyList<int> episodes,
        out string? reason)
    {
        var (cleanTitle, year) = StripYearAndReleaseTags(title);
        if (string.IsNullOrWhiteSpace(cleanTitle))
        {
            reason = TvParsingMessages.EmptyTitleReason;
            return null;
        }

        reason = null;
        return new ParsedTvFile
        {
            Title = cleanTitle,
            Year = year,
            Season = season,
            Episodes = episodes
        };
    }

    private static string TitleFromStemOrFolder(
        string normalisedStem,
        int matchIndex,
        string normalisedParent,
        string normalisedGrandparent,
        bool isSeasonFolderParent)
    {
        var titleSegment = matchIndex > 0
            ? normalisedStem.Substring(0, matchIndex).Trim().TrimEnd('-').Trim()
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(titleSegment))
        {
            return titleSegment;
        }

        if (isSeasonFolderParent && !string.IsNullOrWhiteSpace(normalisedGrandparent))
        {
            return normalisedGrandparent;
        }

        return string.IsNullOrWhiteSpace(normalisedParent) ? normalisedGrandparent : normalisedParent;
    }

    private static bool IsSeasonFolder(string normalisedParent)
    {
        return SeasonFolderToken.IsMatch(normalisedParent) ||
            string.Equals(normalisedParent, "Specials", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Title, int? Year) StripYearAndReleaseTags(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (string.Empty, null);
        }

        var working = raw.Trim();
        int? year = null;

        var yearMatches = TvParsingPatterns.Year.Matches(working);
        if (yearMatches.Count > 0)
        {
            var last = yearMatches[yearMatches.Count - 1];
            if (last.Index > 0)
            {
                year = int.Parse(last.Groups[1].Value, CultureInfo.InvariantCulture);
                working = working.Substring(0, last.Index).Trim();
            }
        }

        var tokens = working.Split([' '], StringSplitOptions.RemoveEmptyEntries);
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

        if (keepLength < tokens.Length)
        {
            working = string.Join(' ', tokens, 0, keepLength);
        }

        return (working.Trim().TrimEnd('-').Trim(), year);
    }
}
