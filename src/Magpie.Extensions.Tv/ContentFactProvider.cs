using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Models.Releases;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.Tv;

internal sealed class ContentFactProvider : IContentFactProvider
{
    public bool CanProvideFacts(ContentTarget target) => string.Equals(
        target.Item.ContentType,
        Constants.ContentTypeSlug,
        StringComparison.Ordinal);

    public Task<IReadOnlyList<IReadOnlyList<ContentFact>>> GetFactsAsync(
        ContentTarget target,
        CancellationToken cancellationToken = default)
    {
        ContentHierarchyItem? show = null;
        ContentHierarchyItem? season = null;
        ContentHierarchyItem? episode = null;
        foreach (var node in target.Lineage)
        {
            if (string.Equals(
                node.Level,
                Constants.ShowLevelSlug,
                StringComparison.OrdinalIgnoreCase))
            {
                show = node;
            }
            else if (string.Equals(
                node.Level,
                Constants.SeasonLevelSlug,
                StringComparison.OrdinalIgnoreCase))
            {
                season = node;
            }
            else if (string.Equals(
                node.Level,
                Constants.EpisodeLevelSlug,
                StringComparison.OrdinalIgnoreCase))
            {
                episode = node;
            }
        }

        if (episode is null && season is null)
        {
            return Task.FromResult<IReadOnlyList<IReadOnlyList<ContentFact>>>([]);
        }

        MaterializedMetadata? showMetadata = null;
        if (show is not null)
        {
            target.Metadata.TryGetValue(show.Id, out showMetadata);
        }

        var showTitle = showMetadata?.Title ?? show?.Name;
        var rows = new List<(
            string? LeftValue,
            string? LeftSource,
            string? LeftId,
            string? RightValue,
            string? Season,
            string? Episode)>();
        if (episode is not null)
        {
            var episodeTitle = target.Metadata.TryGetValue(episode.Id, out var episodeMetadata)
                ? episodeMetadata.Title
                : episode.Name;
            if (showTitle is not null && episodeTitle is not null)
            {
                rows.Add((showTitle, null, null, episodeTitle, null, null));
            }

            if (season?.Ordinal is { } seasonNumber && episode.Ordinal is { } episodeNumber)
            {
                var code = string.Format(CultureInfo.InvariantCulture, "S{0:D2}E{1:D2}", seasonNumber, episodeNumber);
                var seasonText = seasonNumber.ToString(CultureInfo.InvariantCulture);
                var episodeText = episodeNumber.ToString(CultureInfo.InvariantCulture);
                if (showTitle is not null)
                {
                    rows.Add((showTitle, null, null, code, seasonText, episodeText));
                }

                if (show is not null)
                {
                    foreach (var externalId in showMetadata?.ExternalIds ?? [])
                    {
                        if (!string.IsNullOrWhiteSpace(externalId.Source) &&
                            !string.IsNullOrWhiteSpace(externalId.Value))
                        {
                            rows.Add((
                                null,
                                externalId.Source.Trim(),
                                externalId.Value.Trim(),
                                code,
                                seasonText,
                                episodeText));
                        }
                    }
                }
            }

            foreach (var externalId in episodeMetadata?.ExternalIds ?? [])
            {
                if (!string.IsNullOrWhiteSpace(externalId.Source) && !string.IsNullOrWhiteSpace(externalId.Value))
                {
                    rows.Add((null, externalId.Source.Trim(), externalId.Value.Trim(), null, null, null));
                }
            }
        }
        else if (season is not null && season.Ordinal is { } seasonNumber)
        {
            var seasonText = seasonNumber.ToString(CultureInfo.InvariantCulture);
            var seasonLabel = "Season " + seasonText;
            if (showTitle is not null)
            {
                rows.Add((showTitle, null, null, seasonLabel, seasonText, null));
            }

            if (show is not null)
            {
                foreach (var externalId in showMetadata?.ExternalIds ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(externalId.Source) && !string.IsNullOrWhiteSpace(externalId.Value))
                    {
                        rows.Add((
                            null,
                            externalId.Source.Trim(),
                            externalId.Value.Trim(),
                            seasonLabel,
                            seasonText,
                            null));
                    }
                }
            }

            foreach (var externalId in target.Metadata.TryGetValue(season.Id, out var seasonMetadata)
                ? seasonMetadata.ExternalIds ?? []
                : [])
            {
                if (!string.IsNullOrWhiteSpace(externalId.Source) && !string.IsNullOrWhiteSpace(externalId.Value))
                {
                    rows.Add((null, externalId.Source.Trim(), externalId.Value.Trim(), null, null, null));
                }
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var factSets = new List<IReadOnlyList<ContentFact>>();
        foreach (var row in rows)
        {
            var facts = new List<ContentFact>();
            if (row.LeftValue is not null)
            {
                facts.Add(new ContentFact { Value = row.LeftValue });
            }
            else if (row.LeftSource is not null && row.LeftId is not null)
            {
                facts.Add(new ContentFact
                {
                    Facets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [row.LeftSource] = row.LeftId,
                    },
                });
            }

            if (row.RightValue is not null)
            {
                var facets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (row.Season is not null)
                {
                    facets["season"] = row.Season;
                }

                if (row.Episode is not null)
                {
                    facets["episode"] = row.Episode;
                }

                facts.Add(new ContentFact { Value = row.RightValue, Facets = facets });
            }

            if (facts.Count == 0)
            {
                continue;
            }

            var key = string.Join(
                "\u001f",
                facts.Select(fact =>
                    (fact.Value ?? string.Empty) +
                    "\u001e" +
                    string.Join(
                        "\u001d",
                        fact.Facets.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                            .Select(pair => pair.Key + "=" + pair.Value))));
            if (seen.Add(key))
            {
                factSets.Add(facts);
            }
        }

        return Task.FromResult<IReadOnlyList<IReadOnlyList<ContentFact>>>(factSets);
    }
}
