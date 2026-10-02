using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models.Data;

namespace Magpie.Extensions.Wikidata;

internal sealed class EntityResolver(IWikidataClient client, IQueryCoordinator cache) : IEntityResolver
{
    private sealed record ExternalSource(string Source, string PropertyId);

    private sealed record ItemCoordinates(int? SeasonNumber, int? EpisodeNumber);

    [SuppressMessage("Ordering", "SA1201", Justification = "Nested types come first in this codebase.")]
    public async Task<EntityResolution> ResolveContentAsync(
        EntityKind kind,
        ContentHierarchyItem item,
        IReadOnlyList<MetadataContribution> existingMetadata,
        IReadOnlyList<MetadataContribution> ancestorMetadata,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveSubjectAsync(kind, existingMetadata, context, cancellationToken)
            .ConfigureAwait(false);
        if (resolution.Kind != EntityResolutionKind.NotFound || kind is not (EntityKind.Season or EntityKind.Episode))
        {
            return resolution;
        }

        return await ResolveCoordinateAsync(
            kind,
            item,
            existingMetadata,
            ancestorMetadata,
            context,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<EntityResolution> ResolveSubjectAsync(
        EntityKind kind,
        IEnumerable<MetadataContribution> existingMetadata,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var contributions = existingMetadata as IReadOnlyList<MetadataContribution> ?? [.. existingMetadata];
        var direct = FindExternalId(contributions, Constants.ProviderType);
        if (direct.TryNormalizeQid(out var qid))
        {
            return Resolved(qid);
        }

        foreach (var source in ExternalSources(kind))
        {
            var value = FindExternalId(contributions, source.Source);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var resolution = await ResolveExternalAsync(
                kind,
                source.PropertyId,
                value,
                context,
                cancellationToken).ConfigureAwait(false);
            if (resolution.Kind != EntityResolutionKind.NotFound)
            {
                return resolution;
            }
        }

        return NotFound();
    }

    private static ItemCoordinates ParseItemCoordinates(ContentHierarchyItem item)
    {
        int? season = null;
        int? episode = null;
        if (!string.IsNullOrWhiteSpace(item.AlignmentKey))
        {
            var parts = item.AlignmentKey.Split(':');
            if (parts.Length == 2 && parts[0] == "season" && TryPositive(parts[1], out var seasonValue))
            {
                season = seasonValue;
            }
            else if (parts.Length == 3 &&
                parts[0] == "episode" &&
                TryPositive(parts[1], out seasonValue) &&
                TryPositive(parts[2], out var episodeValue))
            {
                season = seasonValue;
                episode = episodeValue;
            }
        }

        if (!string.IsNullOrWhiteSpace(item.Slug))
        {
            var slug = item.Slug.Trim().ToLowerInvariant();
            if (slug.StartsWith("season-", StringComparison.Ordinal) &&
                TryPositive(slug.Substring("season-".Length), out var seasonValue))
            {
                season = MergeCoordinate(season, seasonValue);
            }
            else if (slug.StartsWith("s", StringComparison.Ordinal))
            {
                var separator = slug.IndexOf('e');
                if (separator > 1 &&
                    TryPositive(slug.Substring(1, separator - 1), out seasonValue) &&
                    TryPositive(slug.Substring(separator + 1), out var episodeValue))
                {
                    season = MergeCoordinate(season, seasonValue);
                    episode = MergeCoordinate(episode, episodeValue);
                }
            }
        }

        return new ItemCoordinates(season, episode);
    }

    private static int? MergeCoordinate(int? current, int value) => current is null || current == value ? value : -1;

    private static bool TrySingleCoordinate(IEnumerable<int> values, out int value)
    {
        value = 0;
        var distinct = values.Where(candidate => candidate > 0).Distinct().ToArray();
        if (distinct.Length != 1)
        {
            return false;
        }

        value = distinct[0];
        return true;
    }

    private static bool TryPositive(string value, out int number)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
    }

    private static EntityResolution ResolutionFromRows(
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows)
    {
        var qids = (
            from row in rows
            let value = row.TryGetValue("item", out var binding) ? binding?.Value.ToQid() : null
            where value is not null
            select value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        return qids.Length switch
        {
            0 => NotFound(),
            1 => Resolved(qids[0]),
            _ => new EntityResolution { Kind = EntityResolutionKind.Ambiguous }
        };
    }

    private static string? FindExternalId(IEnumerable<MetadataContribution> contributions, string source)
    {
        return contributions
            .SelectMany(contribution => contribution.ExternalIds
                .Where(externalId => string.Equals(externalId.Source, source, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(externalId.Value))
                .Select(externalId => new { contribution.ProviderType, externalId.Value }))
            .OrderByDescending(match => match.ProviderType == Constants.ProviderType)
            .Select(match => match.Value.Trim())
            .FirstOrDefault();
    }

    private static IReadOnlyList<ExternalSource> ExternalSources(EntityKind kind) => kind switch
    {
        EntityKind.Movie =>
        [
            new ExternalSource(Constants.TmdbSource, Constants.TmdbMoviePropertyId),
            new ExternalSource(Constants.ImdbSource, Constants.ImdbPropertyId)
        ],
        EntityKind.Show =>
        [
            new ExternalSource(Constants.TmdbSource, Constants.TmdbShowPropertyId),
            new ExternalSource(Constants.ImdbSource, Constants.ImdbPropertyId),
            new ExternalSource(Constants.TvdbSource, Constants.TvdbShowPropertyId)
        ],
        EntityKind.Season =>
        [
            new ExternalSource(Constants.TmdbSource, Constants.TmdbSeasonPropertyId),
            new ExternalSource(Constants.TvdbSource, Constants.TvdbSeasonPropertyId)
        ],
        EntityKind.Episode =>
        [
            new ExternalSource(Constants.TmdbSource, Constants.TmdbEpisodePropertyId),
            new ExternalSource(Constants.ImdbSource, Constants.ImdbPropertyId),
            new ExternalSource(Constants.TvdbSource, Constants.TvdbEpisodePropertyId)
        ],
        EntityKind.Person =>
        [
            new ExternalSource(Constants.TmdbSource, Constants.TmdbPersonPropertyId),
            new ExternalSource(Constants.ImdbSource, Constants.ImdbPropertyId)
        ],
        EntityKind.Studio =>
        [
            new ExternalSource(Constants.TmdbSource, Constants.TmdbCompanyPropertyId)
        ],
        _ => []
    };

    private static string? FindBestRankEntityId(EntityDto? entity, string propertyId)
    {
        if (entity?.Claims is null || !entity.Claims.TryGetValue(propertyId, out var statements))
        {
            return null;
        }

        var candidates = statements.Where(statement => statement.Rank != "deprecated").ToArray();
        if (candidates.Any(statement => statement.Rank == "preferred"))
        {
            candidates = [.. candidates.Where(statement => statement.Rank == "preferred")];
        }

        foreach (var statement in candidates)
        {
            var qid = statement.Mainsnak.EntityId();
            if (qid is not null)
            {
                return qid;
            }
        }

        return null;
    }

    private static EntityResolution Resolved(string qid) => new() { Kind = EntityResolutionKind.Resolved, Qid = qid };

    private static EntityResolution NotFound() => new() { Kind = EntityResolutionKind.NotFound };

    private async Task<EntityResolution> ResolveExternalAsync(
        EntityKind kind,
        string propertyId,
        string value,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var cacheKey = string.Join("|", kind, propertyId, value.Trim().ToUpperInvariant());
        return await cache.GetIdentityAsync(
            cacheKey,
            async token =>
            {
                var request = SparqlQueryBuilder.BuildExternalIdentity(kind, propertyId, value.Trim());
                var rows = await client.ExecuteSparqlAsync(request, context, token).ConfigureAwait(false);
                return ResolutionFromRows(rows);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<EntityResolution> ResolveCoordinateAsync(
        EntityKind kind,
        ContentHierarchyItem item,
        IReadOnlyList<MetadataContribution> existingMetadata,
        IReadOnlyList<MetadataContribution> ancestorMetadata,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var ancestorQids = (
            from contribution in ancestorMetadata
            from external in contribution.ExternalIds
            where string.Equals(external.Source, Constants.ProviderType, StringComparison.OrdinalIgnoreCase)
            let value = external.Value.ToQid()
            where value is not null
            select value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ancestorQids.Count == 0)
        {
            return NotFound();
        }

        var itemCoordinates = ParseItemCoordinates(item);
        if (itemCoordinates.SeasonNumber == -1 || itemCoordinates.EpisodeNumber == -1)
        {
            return NotFound();
        }

        var storedOrdinals = (
            from contribution in existingMetadata
            let value = contribution.SequenceNumber
            where value > 0
            select value.Value).ToList();
        if (item.Ordinal > 0)
        {
            storedOrdinals.Add(item.Ordinal.Value);
        }

        var ordinals = itemCoordinates.EpisodeNumber is > 0
            ? storedOrdinals.Append(itemCoordinates.EpisodeNumber.Value).ToArray()
            : itemCoordinates.SeasonNumber is > 0 && kind == EntityKind.Season
                ? [.. storedOrdinals, itemCoordinates.SeasonNumber.Value]
                : [.. storedOrdinals];
        if (!TrySingleCoordinate(ordinals, out var ordinal))
        {
            return NotFound();
        }

        var parentQid = ancestorQids[0];
        string? showQid = null;
        if (kind == EntityKind.Episode)
        {
            showQid = ancestorQids.Skip(1).FirstOrDefault();
            if (showQid is null)
            {
                var season = await cache.GetEntityAsync(parentQid, context, cancellationToken)
                    .ConfigureAwait(false);
                showQid = FindBestRankEntityId(season, Constants.PartOfSeriesPropertyId);
            }

            var seasonCoordinates = (
                from contribution in ancestorMetadata
                let value = contribution.SequenceNumber
                where value > 0
                select value.Value).ToList();
            if (itemCoordinates.SeasonNumber is > 0)
            {
                seasonCoordinates.Add(itemCoordinates.SeasonNumber.Value);
            }

            if (showQid is null ||
                seasonCoordinates.Count > 0 && !TrySingleCoordinate(seasonCoordinates, out _))
            {
                return NotFound();
            }
        }

        var key = string.Join("|", "coordinate", kind, parentQid, showQid, ordinal);
        return await cache.GetIdentityAsync(
            key,
            async token =>
            {
                var request = SparqlQueryBuilder.BuildCoordinateIdentity(kind, parentQid, ordinal, showQid);
                var rows = await client.ExecuteSparqlAsync(request, context, token).ConfigureAwait(false);
                return ResolutionFromRows(rows);
            },
            cancellationToken).ConfigureAwait(false);
    }
}
