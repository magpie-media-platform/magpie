using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Magpie.Extensions.Tv.Parsing;

internal static class TvReleaseTitleParsingExtensions
{
    private const int MaxExpandedEpisodeRange = 200;

    private static readonly Regex LongFormSeasonEpisode = new(
        @"(?<![A-Za-z0-9])season[\s._-]*0*(?<season>\d{1,3})" +
        @"[\s._-]*episode[\s._-]*0*(?<episode>\d{1,4})(?![A-Za-z0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SeasonOnlyToken = new(
        @"(?<![A-Za-z0-9])(?:s|(?:season|series)[\s\.\-_]*)0*(?<season>\d{1,3})(?![A-Za-z0-9e])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SeasonContinuation = new(
        @"[\s.,_-]+(?:s(?:eason)?[\s._-]*)?0*(?<season>\d{1,3})(?![A-Za-z0-9e])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MultiWhitespace = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static ParsedTvRelease ParseTvReleaseTitle(this string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new ParsedTvRelease([], [], null, []);
        }

        var source = title;

        IReadOnlyList<int> seasons = [];
        IReadOnlyList<int> episodes = [];
        var signalIndex = int.MaxValue;

        var sxxEyy = TvParsingPatterns.SeasonEpisode.Match(source);
        var numX = TvParsingPatterns.NumXNum.Match(source);
        var longForm = LongFormSeasonEpisode.Match(source);
        var seasonOnly = SeasonOnlyToken.Match(source);

        if (sxxEyy.Success)
        {
            seasons = [int.Parse(sxxEyy.Groups["season"].Value, CultureInfo.InvariantCulture)];
            episodes = sxxEyy.ParseEpisodes();
            signalIndex = sxxEyy.Index;
        }
        else if (numX.Success)
        {
            seasons = [int.Parse(numX.Groups["season"].Value, CultureInfo.InvariantCulture)];
            episodes = numX.ParseEpisodes();
            signalIndex = numX.Index;
        }
        else if (longForm.Success)
        {
            seasons = [int.Parse(longForm.Groups["season"].Value, CultureInfo.InvariantCulture)];
            episodes = [int.Parse(longForm.Groups["episode"].Value, CultureInfo.InvariantCulture)];
            signalIndex = longForm.Index;
        }
        else if (seasonOnly.Success)
        {
            var startSeason = int.Parse(seasonOnly.Groups["season"].Value, CultureInfo.InvariantCulture);
            var allowBareContinuation =
                seasonOnly.Value.StartsWith("season", StringComparison.OrdinalIgnoreCase) ||
                seasonOnly.Value.StartsWith("series", StringComparison.OrdinalIgnoreCase);
            seasons = ParseSeasonValues(
                source,
                seasonOnly.Index + seasonOnly.Length,
                startSeason,
                allowBareContinuation);
            signalIndex = seasonOnly.Index;
        }

        var titleSource = signalIndex == int.MaxValue
            ? source
            : source.Substring(0, Math.Min(signalIndex, source.Length));

        return new ParsedTvRelease(
            seasons,
            episodes,
            signalIndex == int.MaxValue ? null : signalIndex,
            Tokenise(StripYear(Normalise(titleSource))));
    }

    internal static IReadOnlyList<string> TokeniseName(this string? name) =>
        string.IsNullOrWhiteSpace(name) ? [] : Tokenise(StripYear(Normalise(name)));

    internal static IReadOnlyList<int> ParseEpisodes(this Match match) =>
        ExpandOrSingleton(
            int.Parse(match.Groups["episode"].Value, CultureInfo.InvariantCulture),
            ParseOptionalInt(match.Groups["end"].Value));

    private static int? ParseOptionalInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static IReadOnlyList<int> ExpandOrSingleton(int start, int? end)
    {
        if (end is not { } endValue ||
            endValue <= start ||
            endValue - start > MaxExpandedEpisodeRange)
        {
            return [start];
        }

        var values = new int[endValue - start + 1];
        for (var value = start; value <= endValue; value++)
        {
            values[value - start] = value;
        }

        return values;
    }

    private static IReadOnlyList<int> ParseSeasonValues(
        string source,
        int startIndex,
        int startSeason,
        bool allowBareContinuation)
    {
        var seasons = new List<int> { startSeason };
        var cursor = startIndex;
        var rangeEnd = (int?)null;
        while (cursor < source.Length)
        {
            var match = SeasonContinuation.Match(source, cursor);
            if (!match.Success || match.Index != cursor)
            {
                break;
            }

            var prefix = match.Value.TrimStart(' ', '.', ',', '_', '-');
            var isSeasonPrefixed = prefix.StartsWith("s", StringComparison.OrdinalIgnoreCase);
            if (!isSeasonPrefixed && !allowBareContinuation)
            {
                break;
            }

            var seasonGroup = match.Groups["season"];
            var season = int.Parse(seasonGroup.Value, CultureInfo.InvariantCulture);
            if (season > 100)
            {
                break;
            }

            var separator = match.Value[..(seasonGroup.Index - match.Index)];
            var isDashedRange = separator.Contains('-', StringComparison.Ordinal);
            if (isDashedRange)
            {
                if (season <= startSeason)
                {
                    break;
                }

                rangeEnd = season;
                cursor = match.Index + match.Length;
                break;
            }

            if (seasons.Contains(season))
            {
                break;
            }

            seasons.Add(season);
            cursor = match.Index + match.Length;
        }

        if (rangeEnd is { } end)
        {
            var expanded = new int[end - startSeason + 1];
            for (var value = startSeason; value <= end; value++)
            {
                expanded[value - startSeason] = value;
            }

            return expanded;
        }

        return seasons;
    }

    private static string StripYear(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var working = raw.Trim();
        Match? last = null;
        foreach (Match candidate in TvParsingPatterns.Year.Matches(working))
        {
            last = candidate;
        }

        if (last is { Index: > 0 })
        {
            var before = working.Substring(0, last.Index);
            var after = working.Substring(last.Index + last.Length);
            working = CollapseWhitespace((before + " " + after).Trim());
        }

        return working;
    }

    private static IReadOnlyList<string> Tokenise(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var tokens = new List<string>();
        foreach (var raw in text.Split([' '], StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim().ToLowerInvariant();
            if (token.Length > 0)
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    private static string CollapseWhitespace(string value) => MultiWhitespace.Replace(value, " ");

    private static string Normalise(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var folded = FoldDiacritics(input);
        var builder = new StringBuilder(folded.Length);
        foreach (var c in folded)
        {
            switch (c)
            {
                case '\'':
                case '’':
                    break;
                case '-':
                case '&':
                case '.':
                case '_':
                case '(':
                case ')':
                case '[':
                case ']':
                case '{':
                case '}':
                case ':':
                case ',':
                case '!':
                case '?':
                case '"':
                    builder.Append(' ');
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return CollapseWhitespace(builder.ToString()).Trim();
    }

    private static string FoldDiacritics(string input)
    {
        var decomposed = input.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
