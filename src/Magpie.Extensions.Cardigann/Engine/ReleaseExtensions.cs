using System;
using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Extensions.Cardigann.Engine;

internal static class ReleaseExtensions
{
    private const string ImdbSource = "imdb";
    private const string TmdbSource = "tmdb";
    private const string TvdbSource = "tvdb";
    private const string TvMazeSource = "tvmaze";
    private const string TraktSource = "trakt";

    public static void CaptureProviderIds(this Release release, IReadOnlyDictionary<string, string?> fields)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(fields);

        AddIfPresent(release, ImdbSource, FirstNonEmpty(fields, "imdbid", "imdb"));
        AddIfPresent(release, TmdbSource, Lookup(fields, "tmdbid"));
        AddIfPresent(release, TvdbSource, Lookup(fields, "tvdbid"));
        AddIfPresent(release, TvMazeSource, Lookup(fields, "tvmazeid"));
        AddIfPresent(release, TraktSource, Lookup(fields, "traktid"));
    }

    private static void AddIfPresent(Release release, string source, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var value = raw.Trim();
        release.ExternalIds.Add(new ExternalId { Ordinal = release.ExternalIds.Count, Source = source, Value = value });
    }

    private static string? FirstNonEmpty(IReadOnlyDictionary<string, string?> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Lookup(fields, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? Lookup(IReadOnlyDictionary<string, string?> fields, string key)
    {
        return fields.TryGetValue(key, out var value) ? value : null;
    }
}
