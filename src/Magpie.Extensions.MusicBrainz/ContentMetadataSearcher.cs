using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz.Mapping;
using Magpie.Extensions.MusicBrainz.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.MusicBrainz;

internal sealed class ContentMetadataSearcher(
    ISettingsResolver settingsResolver,
    IQueryPlanner queryPlanner,
    IQueryExecutor queryExecutor,
    IQueryCoordinator cache,
    IMusicBrainzClient client,
    ICoverArtArchiveClient coverClient,
    ILogger<ContentMetadataSearcher> logger) : IContentHierarchyMetadataAdvancedSearcher
{
    private const int CoverSize = 500;

    public string ProviderType => Constants.ProviderType;

    public IEnumerable<string> SupportedContentTypes => [Constants.ContentTypeSlug];

    public async Task<IEnumerable<MetadataSearchCandidate>> SearchAsync(
        ContentHierarchyLevelDefinition levelDefinition,
        string searchTerm,
        string defaultLanguagePreference,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var target = ResolveTarget(levelDefinition.Slug);
        if (target is null || string.IsNullOrWhiteSpace(searchTerm))
        {
            return [];
        }

        var settings = await settingsResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        var result = await ExecutePlanAsync(
            new QueryRequest
            {
                Target = target.Value,
                Criteria = new MetadataCriteria(),
                RequestedPage = page,
                MaxResults = QueryLimits.DefaultMaxResults,
                Settings = settings,
                IsOrdinary = true,
                OrdinaryTerm = searchTerm
            },
            cancellationToken).ConfigureAwait(false);
        return [.. result.Hits.Select(hit => hit.ToCandidate())];
    }

    public async Task<MetadataSearchPage> AdvancedSearchAsync(
        string contentType,
        ContentHierarchyItemCriteria query,
        int maxResults,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(contentType, Constants.ContentTypeSlug, StringComparison.Ordinal))
        {
            logger.LogDebug("MusicBrainz does not support content type {ContentType}", contentType);
            return MetadataSearchPage.NotSupported;
        }

        if (!MatchesScope(query.ContentType.And.ToArray(), contentType))
        {
            return EmptySupported();
        }

        var metadata = query.MergeMetadata();
        var targets = ResolveTargets(query, metadata);
        if (targets.Count == 0)
        {
            return EmptySupported();
        }

        var settings = await settingsResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        return await SearchTargetsAsync(
            targets,
            metadata,
            1,
            Math.Max(1, maxResults),
            settings,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<MetadataSearchPage> SearchTargetsAsync(
        IReadOnlyList<QueryTarget> targets,
        MetadataCriteria criteria,
        int requestedPage,
        int maxResults,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var results = new List<QueryResult>(targets.Count);
        var hits = new List<SearchHit>();
        var seen = new HashSet<(QueryTarget Target, string Mbid)>();
        var hasMore = false;
        for (var index = 0; index < targets.Count; index++)
        {
            var remaining = maxResults - hits.Count;
            if (remaining == 0)
            {
                hasMore = true;
                break;
            }

            var result = await ExecutePlanAsync(
                new QueryRequest
                {
                    Target = targets[index],
                    Criteria = criteria,
                    RequestedPage = requestedPage,
                    MaxResults = remaining,
                    Settings = settings
                },
                cancellationToken).ConfigureAwait(false);
            results.Add(result);
            hasMore |= result.PageInfo.HasMore;
            foreach (var hit in result.Hits)
            {
                if (seen.Add((hit.Target, hit.PrimaryMbid)))
                {
                    hits.Add(hit);
                }
            }

            if (hits.Count == maxResults && index < targets.Count - 1)
            {
                hasMore = true;
                break;
            }
        }

        var matches = hits.Select(hit => hit.ToCandidate()).ToArray();
        return new MetadataSearchPage
        {
            Supported = results.Any(result => result.Supported),
            Matches = matches,
            PageInfo = new ProviderSearchPageInfo { RequestedPage = requestedPage, HasMore = hasMore },
            Warnings = [.. results.SelectMany(result => result.Warnings).Distinct()]
        };
    }

    public async Task<MetadataSearchCandidate?> HydrateAsync(
        MetadataSearchCandidate candidate,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        if (candidate.ProviderType != ProviderType ||
            !string.Equals(candidate.ContentType, Constants.ContentTypeSlug, StringComparison.Ordinal) ||
            !Guid.TryParse(candidate.ExternalId, out var candidateMbid))
        {
            return null;
        }

        var kind = ResolveEntityKind(candidate);
        if (kind is null)
        {
            return null;
        }

        var mbid = candidateMbid.ToString("D", CultureInfo.InvariantCulture);
        var settings = await settingsResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        var key = new EntityCacheKey { Kind = kind.Value, Mbid = mbid, Profile = LookupProfile.Hydration };
        var fetched = await cache.GetEntityAsync(
            key,
            token => client.GetEntityAsync(key, settings, token),
            cancellationToken).ConfigureAwait(false);
        if (fetched is not { Outcome: FetchOutcome.Success, Value: { } entity })
        {
            return null;
        }

        var hit = kind switch
        {
            EntityKind.Artist => entity.Artist.ToHit(),
            EntityKind.ReleaseGroup => entity.ReleaseGroup.ToHit(),
            EntityKind.Recording => entity.Recording.ToHit(),
            _ => null
        };
        if (hit is null)
        {
            return null;
        }

        if (kind == EntityKind.ReleaseGroup)
        {
            var labels = await queryExecutor
                .GetRepresentativeReleaseLabelsAsync(hit, settings, cancellationToken)
                .ConfigureAwait(false);
            if (labels is { Outcome: FetchOutcome.Success, Value: { } references })
            {
                hit = hit with { LabelReferences = references };
            }
        }

        Uri? thumbnail = null;
        if (kind == EntityKind.ReleaseGroup)
        {
            var coverKey = new CoverCacheKey { ReleaseGroupMbid = mbid, ThumbnailSize = CoverSize };
            var cover = await cache.GetCoverAsync(
                coverKey,
                token => coverClient.GetReleaseGroupFrontAsync(mbid, CoverSize, settings, token),
                cancellationToken).ConfigureAwait(false);
            thumbnail = cover.Outcome == FetchOutcome.Success ? cover.Value : null;
        }

        return hit.ToCandidate(thumbnail);
    }

    private static QueryTarget? ResolveTarget(string level)
    {
        if (string.Equals(level, Constants.ArtistLevelSlug, StringComparison.Ordinal))
        {
            return QueryTarget.Artist;
        }

        if (string.Equals(level, Constants.AlbumLevelSlug, StringComparison.Ordinal))
        {
            return QueryTarget.Album;
        }

        return string.Equals(level, Constants.TrackLevelSlug, StringComparison.Ordinal)
            ? QueryTarget.Track
            : null;
    }

    private static IReadOnlyList<QueryTarget> ResolveTargets(
        ContentHierarchyItemCriteria query,
        MetadataCriteria criteria)
    {
        var levelCriteria = query.Level.And.ToArray();
        var targets = AllTargets()
            .Where(target => MatchesScope(levelCriteria, TargetLevel(target)))
            .ToHashSet();

        var identityGroups = criteria.ExternalIds.Some.Where(group => group.Count > 0).ToArray();
        if (identityGroups.Length == 0)
        {
            return [.. targets.OrderBy(value => value)];
        }

        foreach (var group in identityGroups)
        {
            targets.IntersectWith(group.SelectMany(IdentityTargets));
        }

        return [.. targets.OrderBy(value => value)];
    }

    private static string TargetLevel(QueryTarget target) => target switch
    {
        QueryTarget.Artist => Constants.ArtistLevelSlug,
        QueryTarget.Album => Constants.AlbumLevelSlug,
        QueryTarget.Track => Constants.TrackLevelSlug,
        _ => string.Empty
    };

    private static MetadataSearchPage EmptySupported() => new()
    {
        Supported = true,
        Matches = [],
        PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false },
        Warnings = []
    };

    private static bool MatchesScope(
        IReadOnlyList<PropertyCriterion<string>> criteria,
        string value)
    {
        foreach (var criterion in criteria)
        {
            var (operation, negate) = criterion.Operator switch
            {
                CriteriaValueOperator.NotEquals => (CriteriaValueOperator.Equals, true),
                CriteriaValueOperator.NotIn => (CriteriaValueOperator.In, true),
                CriteriaValueOperator.NotContainsString => (CriteriaValueOperator.ContainsString, true),
                CriteriaValueOperator.NotStartsWithString => (CriteriaValueOperator.StartsWithString, true),
                CriteriaValueOperator.NotEndsWithString => (CriteriaValueOperator.EndsWithString, true),
                _ => (criterion.Operator, false)
            };
            var values = TextValues(criterion);
            var supported = operation is CriteriaValueOperator.Equals or CriteriaValueOperator.In
                or CriteriaValueOperator.ContainsString or CriteriaValueOperator.ContainsWord
                or CriteriaValueOperator.StartsWithString or CriteriaValueOperator.EndsWithString
                or CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull;
            var needsValues = operation is not
                (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull);
            var negated = negate ^ criterion.Negate;
            if (!supported ||
                (values.Length == 0 && needsValues) ||
                operation.MatchesString(values, value) == negated)
            {
                return false;
            }
        }

        return true;
    }

    private static string[] TextValues(PropertyCriterion<string> criterion) =>
        [.. (criterion.Values ?? [])
            .Append(criterion.Value ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static IEnumerable<QueryTarget> IdentityTargets(ExternalIdCriteria criteria)
    {
        if (!criteria.Source.And.Any())
        {
            var values = criteria.Value.And
                .Where(criterion => !criterion.Negate &&
                    criterion.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In)
                .SelectMany(criterion => criterion.Values?.Append(criterion.Value ?? string.Empty)
                    ?? [criterion.Value ?? string.Empty])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            if (values.Length == 0)
            {
                return AllTargets();
            }

            var embeddedTargets = new List<QueryTarget>();
            foreach (var value in values)
            {
                var separator = value.IndexOf(':');
                if (separator <= 0)
                {
                    return AllTargets();
                }

                var targets = TargetsForSource(value.Substring(0, separator).Trim()).ToArray();
                if (targets.Length == 0)
                {
                    return AllTargets();
                }

                embeddedTargets.AddRange(targets);
            }

            return embeddedTargets;
        }

        if (criteria.Source.And.Any(criterion => criterion.Negate ||
            criterion.Operator is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In)))
        {
            return AllTargets();
        }

        var sourceTargets = AllTargets().ToHashSet();
        foreach (var criterion in criteria.Source.And)
        {
            var criterionTargets = (criterion.Values?.Append(criterion.Value ?? string.Empty)
                ?? [criterion.Value ?? string.Empty])
                .SelectMany(TargetsForSource)
                .ToHashSet();
            sourceTargets.IntersectWith(criterionTargets);
        }

        return sourceTargets;
    }

    private static IEnumerable<QueryTarget> AllTargets()
    {
        return [QueryTarget.Artist, QueryTarget.Album, QueryTarget.Track];
    }

    private static IEnumerable<QueryTarget> TargetsForSource(string source)
    {
        return source.ToLowerInvariant() switch
        {
            Constants.ProviderType => AllTargets(),
            Constants.IsniSource or Constants.IpiSource => [QueryTarget.Artist],
            Constants.IfpiSource => [QueryTarget.Album],
            Constants.IsrcSource => [QueryTarget.Track],
            _ => []
        };
    }

    private static EntityKind? ResolveEntityKind(MetadataSearchCandidate candidate)
    {
        if (string.Equals(candidate.Level, Constants.ArtistLevelSlug, StringComparison.Ordinal))
        {
            return EntityKind.Artist;
        }

        if (string.Equals(candidate.Level, Constants.AlbumLevelSlug, StringComparison.Ordinal))
        {
            return EntityKind.ReleaseGroup;
        }

        if (string.Equals(candidate.Level, Constants.TrackLevelSlug, StringComparison.Ordinal))
        {
            return EntityKind.Recording;
        }

        return null;
    }

    private async Task<QueryResult> ExecutePlanAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        var plan = queryPlanner.Plan(request);
        if (plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.Refused))
        {
            return new QueryResult
            {
                Supported = true,
                Hits = [],
                PageInfo = new ProviderSearchPageInfo
                {
                    RequestedPage = request.RequestedPage,
                    HasMore = false
                }
            };
        }

        return await queryExecutor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
    }
}
