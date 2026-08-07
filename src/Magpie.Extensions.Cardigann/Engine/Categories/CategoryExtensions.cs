using System;
using System.Collections.Generic;

namespace Magpie.Extensions.Cardigann.Engine.Categories;

internal static class CategoryExtensions
{
    public static IReadOnlyList<string>? NarrowToContentCategories(
        this IReadOnlyList<string> indexerCategories,
        IReadOnlyList<string> contentCategories)
    {
        if (indexerCategories.Count == 0)
        {
            return [];
        }

        var bands = ResolveBands(contentCategories);
        if (bands.Count == 0)
        {
            return [.. indexerCategories];
        }

        var recognised = false;
        var filtered = new List<string>();

        foreach (var category in indexerCategories)
        {
            if (TryResolveBand(category, out var band))
            {
                recognised = true;
                if (bands.Contains(band))
                {
                    filtered.Add(category);
                }

                continue;
            }

            filtered.Add(category);
        }

        return filtered.Count == 0 && recognised ? null : filtered;
    }

    public static bool MatchesContentCategories(
        this IReadOnlyList<string> releaseCategories,
        IReadOnlyList<string> contentCategories,
        out bool recognised)
    {
        recognised = false;

        var bands = ResolveBands(contentCategories);
        if (releaseCategories.Count == 0 || bands.Count == 0)
        {
            return true;
        }

        foreach (var category in releaseCategories)
        {
            if (!TryResolveBand(category, out var band))
            {
                continue;
            }

            recognised = true;
            if (bands.Contains(band))
            {
                return true;
            }
        }

        return !recognised;
    }

    private static HashSet<int> ResolveBands(IReadOnlyList<string> contentCategories)
    {
        var bands = new HashSet<int>();
        foreach (var category in contentCategories)
        {
            if (TryResolveBand(category, out var band))
            {
                bands.Add(band);
            }
        }

        return bands;
    }

    private static bool TryResolveBand(string category, out int band)
    {
        band = 0;
        if (string.IsNullOrWhiteSpace(category))
        {
            return false;
        }

        var trimmed = category.Trim();
        if (int.TryParse(trimmed, out var id) && id >= 1000)
        {
            band = id / 1000 * 1000;
            return true;
        }

        var separator = trimmed.IndexOf('/', StringComparison.Ordinal);
        var root = separator >= 0 ? trimmed[..separator] : trimmed;
        band = root.Trim().ToLowerInvariant() switch
        {
            "console" => 1000,
            "movie" or "movies" => 2000,
            "audio" or "music" => 3000,
            "pc" => 4000,
            "tv" or "television" => 5000,
            "adult" or "xxx" => 6000,
            "book" or "books" or "ebook" => 7000,
            "other" => 8000,
            _ => 0,
        };

        return band != 0;
    }
}
