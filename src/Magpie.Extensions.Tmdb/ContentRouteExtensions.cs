using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Magpie.Extensions.Tmdb.QueryEngine;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tmdb;

internal static class ContentRouteExtensions
{
    private static readonly Regex SeasonSlugPattern =
        new("^season-(\\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SeasonFromEpisodeSlugPattern =
        new("^s(\\d+)e\\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EpisodeSlugPattern =
        new("^s\\d+e(\\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static ContentRoute? ResolveContentRoute(this string contentType, string level)
    {
        if (contentType == "movie" && level == "movie")
        {
            return ContentRoute.Movie;
        }

        if (contentType != "tv")
        {
            return null;
        }

        return level switch
        {
            "show" => ContentRoute.TvShow,
            "season" => ContentRoute.TvSeason,
            "episode" => ContentRoute.TvEpisode,
            _ => null,
        };
    }

    internal static string ContentTypeName(this ContentRoute route)
    {
        return route switch
        {
            ContentRoute.Movie => "movie",
            ContentRoute.TvShow => "tv",
            ContentRoute.TvSeason => "tvSeason",
            ContentRoute.TvEpisode => "tvEpisode",
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
    }

    internal static ContentRoute? ResolveDiscoverRoute(this string levelSlug)
    {
        return levelSlug switch
        {
            "movie" => ContentRoute.Movie,
            "show" => ContentRoute.TvShow,
            _ => null,
        };
    }

    public static ContentRoute? ResolveAdvancedSearchRoute(
        this string contentType,
        ContentHierarchyItemCriteria query)
    {
        if (!MatchesValue(query.ContentType.And, contentType))
        {
            return null;
        }

        if (contentType.Equals("movie", StringComparison.OrdinalIgnoreCase))
        {
            return MatchesValue(query.Level.And, "movie") ? ContentRoute.Movie : null;
        }

        if (!contentType.Equals("tv", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!query.Level.And.Any())
        {
            return ContentRoute.TvShow;
        }

        return MatchesValue(query.Level.And, "show") ? ContentRoute.TvShow : null;
    }

    public static string? ResolveCreditsPath(
        this ContentHierarchyItem item,
        IEnumerable<MetadataContribution> existingMetadata,
        ContentRoute route,
        IReadOnlyList<MetadataContribution>? ancestorMetadata,
        ILogger logger)
    {
        var existing = existingMetadata as IReadOnlyCollection<MetadataContribution> ?? [.. existingMetadata];
        var ancestors = ancestorMetadata ?? [];

        var resolvedSeriesId = ancestors.Reverse().FindTmdbId();
        var resolvedSequence = route == ContentRoute.TvSeason
            ? existing.FindSequenceNumber()
            : ancestors.FindSequenceNumber();
        var seasonNumber = resolvedSequence ?? ParseSeasonNumberFromSlug(item.Slug);
        if (resolvedSeriesId is null ||
            seasonNumber is null ||
            !int.TryParse(resolvedSeriesId, out _) ||
            !int.TryParse(seasonNumber, out _))
        {
            logger.LogInformation(
                "TMDb cast skipped for {Level} item {Id}: no series id or season number",
                item.Level,
                item.Id);
            return null;
        }

        if (route == ContentRoute.TvSeason)
        {
            return string.Format(CultureInfo.InvariantCulture, "3/tv/{0}/season/{1}", resolvedSeriesId, seasonNumber);
        }

        var episodeNumber = existing.FindSequenceNumber()
            ?? ParseEpisodeNumberFromSlug(item.Slug);
        if (episodeNumber is null || !int.TryParse(episodeNumber, out _))
        {
            logger.LogInformation("TMDb cast skipped for episode item {Id}: no episode number", item.Id);
            return null;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "3/tv/{0}/season/{1}/episode/{2}",
            resolvedSeriesId,
            seasonNumber,
            episodeNumber);
    }

    private static string? ParseSeasonNumberFromSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var seasonMatch = SeasonSlugPattern.Match(slug);
        if (seasonMatch.Success)
        {
            return NormalizeNumber(seasonMatch.Groups[1].Value);
        }

        var episodeMatch = SeasonFromEpisodeSlugPattern.Match(slug);
        return episodeMatch.Success ? NormalizeNumber(episodeMatch.Groups[1].Value) : null;
    }

    private static bool MatchesValue(IEnumerable<PropertyCriterion<string>> criteria, string candidate)
    {
        return criteria.All(criterion => MatchesValue(criterion, candidate));
    }

    private static bool MatchesValue(PropertyCriterion<string> criterion, string candidate)
    {
        var values = new[] { criterion.Value }
            .Concat(criterion.Values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToList();
        var (operation, operatorNegate) = criterion.Operator switch
        {
            CriteriaValueOperator.NotEquals => (CriteriaValueOperator.Equals, true),
            CriteriaValueOperator.NotIn => (CriteriaValueOperator.In, true),
            CriteriaValueOperator.NotContainsString => (CriteriaValueOperator.ContainsString, true),
            CriteriaValueOperator.NotStartsWithString => (CriteriaValueOperator.StartsWithString, true),
            CriteriaValueOperator.NotEndsWithString => (CriteriaValueOperator.EndsWithString, true),
            _ => (criterion.Operator, false)
        };
        var matched = candidate.MatchesText(operation, values);
        return matched ^ criterion.Negate ^ operatorNegate;
    }

    private static string? ParseEpisodeNumberFromSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var match = EpisodeSlugPattern.Match(slug);
        return match.Success ? NormalizeNumber(match.Groups[1].Value) : null;
    }

    private static string? NormalizeNumber(string value)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }
}
