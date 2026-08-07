using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

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
        "2160p",
        "1080p",
        "720p",
        "576p",
        "480p",
        "4k",
        "uhd",
        "BluRay",
        "BDRip",
        "BRRip",
        "WEB-DL",
        "WEBDL",
        "WEBRip",
        "WEB",
        "HDRip",
        "HDTV",
        "DVDRip",
        "DVD",
        "REMUX",
        "x264",
        "x265",
        "h264",
        "h265",
        "HEVC",
        "AVC",
        "DDP5",
        "DDP5.1",
        "DD5",
        "AC3",
        "AAC",
        "DTS",
        "TrueHD",
        "Atmos",
        "PROPER",
        "REPACK",
        "INTERNAL",
        "LIMITED",
        "MULTI",
        "DUAL"
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

        var normalisedStem = Normalise(stem);
        var normalisedParent = Normalise(parentFolder);
        var normalisedGrandparent = Normalise(grandparentFolder);

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
            var episode = int.Parse(sxxEyyMatch.Groups["episode"].Value, CultureInfo.InvariantCulture);
            var title = TitleFromStemOrFolder(
                normalisedStem,
                sxxEyyMatch.Index,
                normalisedParent,
                normalisedGrandparent,
                isSeasonFolderParent: IsSeasonFolder(normalisedParent));
            return BuildResult(title, season, episode, out reason);
        }

        var numXMatch = TvParsingPatterns.NumXNum.Match(normalisedStem);
        if (numXMatch.Success)
        {
            var season = int.Parse(numXMatch.Groups["season"].Value, CultureInfo.InvariantCulture);
            var episode = int.Parse(numXMatch.Groups["episode"].Value, CultureInfo.InvariantCulture);
            var title = TitleFromStemOrFolder(
                normalisedStem,
                numXMatch.Index,
                normalisedParent,
                normalisedGrandparent,
                isSeasonFolderParent: IsSeasonFolder(normalisedParent));
            return BuildResult(title, season, episode, out reason);
        }

        var seasonFolderMatch = SeasonFolderToken.Match(normalisedParent);
        if (seasonFolderMatch.Success)
        {
            var leafMatch = NumericLeafToken.Match(normalisedStem);
            if (leafMatch.Success)
            {
                var season = int.Parse(seasonFolderMatch.Groups["season"].Value, CultureInfo.InvariantCulture);
                var episode = int.Parse(leafMatch.Groups["episode"].Value, CultureInfo.InvariantCulture);
                return BuildResult(normalisedGrandparent, season, episode, out reason);
            }
        }

        if (string.Equals(parentFolder, "Specials", StringComparison.OrdinalIgnoreCase))
        {
            var leafMatch = NumericLeafToken.Match(normalisedStem);
            if (leafMatch.Success)
            {
                var episode = int.Parse(leafMatch.Groups["episode"].Value, CultureInfo.InvariantCulture);
                return BuildResult(normalisedGrandparent, season: 0, episode, out reason);
            }
        }

        reason = TvParsingMessages.NoSeasonEpisodeReason;
        return null;
    }

    private static string Normalise(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var chars = new char[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            var character = input[i];
            chars[i] = character switch
            {
                '.' or '_' or '(' or ')' or '[' or ']' or '{' or '}' => ' ',
                _ => character
            };
        }

        var stage = new string(chars);
        while (stage.Contains("  "))
        {
            stage = stage.Replace("  ", " ");
        }

        return stage.Trim();
    }

    private static ParsedTvFile? BuildResult(
        string title,
        int season,
        int episode,
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
            Episode = episode
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
        while (keepLength > 0 && ReleaseTagTokens.Contains(tokens[keepLength - 1]))
        {
            keepLength--;
        }

        if (keepLength < tokens.Length)
        {
            working = string.Join(' ', tokens, 0, keepLength);
        }

        return (working.Trim().TrimEnd('-').Trim(), year);
    }
}
