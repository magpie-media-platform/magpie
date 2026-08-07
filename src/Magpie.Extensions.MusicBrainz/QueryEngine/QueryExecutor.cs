using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz.Mapping;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed class QueryExecutor(
    IMusicBrainzClient client,
    IQueryCache cache,
    ICoverArtArchiveClient coverClient,
    ILogger<QueryExecutor> logger) : IQueryExecutor
{
    private const int BrowsePageSize = 100;
    private const int CoverThumbnailSize = 500;
    private const int MaximumRequestTargetBytes = 8 * 1024;
    private const int ArtistResolutionPageSize = 100;

    private const int ScanBatchSize = 100;

    private static readonly string ScanTruncationWarning =
        "musicbrainz results may be incomplete because the upstream result set was truncated.";

    private static readonly string OversizedQueryWarning =
        "musicbrainz rejected a query larger than 8 KiB before transport.";

    private readonly AsyncLocal<ExecutionTrace?> _currentTrace = new();
    private readonly AsyncLocal<int?> _currentNodeId = new();
    private readonly AsyncLocal<int?> _currentStageNodeId = new();

    public async Task<QueryResult> ExecuteAsync(QueryPlan plan, CancellationToken cancellationToken)
    {
        var previousTrace = _currentTrace.Value;
        var previousNodeId = _currentNodeId.Value;
        var previousStageNodeId = _currentStageNodeId.Value;
        _currentTrace.Value = logger.IsEnabled(LogLevel.Trace) ? new ExecutionTrace() : null;
        _currentNodeId.Value = null;
        _currentStageNodeId.Value = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (PlannedEmpty(plan))
            {
                TraceStageSkipped(
                    "seed",
                    plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.Refused)
                        ? "a required criterion is unsupported"
                        : plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty)
                            ? "criteria planned an empty result"
                            : "criteria do not provide a positive bounded seed");
            }

            var result = await ExecuteCoreAsync(plan, cancellationToken).ConfigureAwait(false);
            TraceResult(plan, result, stopwatch.Elapsed);

            return result;
        }
        finally
        {
            _currentTrace.Value = previousTrace;
            _currentNodeId.Value = previousNodeId;
            _currentStageNodeId.Value = previousStageNodeId;
        }
    }

    public async Task<FetchResult<IReadOnlyList<LabelReference>>> GetRepresentativeReleaseLabelsAsync(
        SearchHit hit,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var groupKey = new EntityCacheKey
        {
            Kind = EntityKind.ReleaseGroup,
            Mbid = hit.PrimaryMbid,
            Profile = LookupProfile.Search,
            Capabilities = LookupCapability.Releases
        };
        var groupFetched = await GetEntityAsync(groupKey, settings, cancellationToken).ConfigureAwait(false);
        if (groupFetched is not { Outcome: FetchOutcome.Success, Value.ReleaseGroup: { } releaseGroup })
        {
            return Failed<IReadOnlyList<LabelReference>>(groupFetched);
        }

        if (!MatchesMbid(releaseGroup.Id, hit.PrimaryMbid))
        {
            return Outcome<IReadOnlyList<LabelReference>>(FetchOutcome.Malformed, groupFetched.StatusCode);
        }

        if (releaseGroup.Releases is null)
        {
            return Outcome<IReadOnlyList<LabelReference>>(FetchOutcome.Malformed);
        }

        if (releaseGroup.Releases.Count == 0)
        {
            return Success<IReadOnlyList<LabelReference>>([]);
        }

        var representative = releaseGroup.SelectRepresentativeRelease();
        if (representative is null || !Guid.TryParse(representative.Id, out var releaseMbid))
        {
            return Outcome<IReadOnlyList<LabelReference>>(FetchOutcome.Malformed);
        }

        if (representative.LabelInfo is not null)
        {
            return Success(representative.LabelReferences());
        }

        var releaseKey = new EntityCacheKey
        {
            Kind = EntityKind.Release,
            Mbid = releaseMbid.ToString("D"),
            Profile = LookupProfile.Search,
            Capabilities = LookupCapability.Labels
        };
        var releaseFetched = await GetEntityAsync(releaseKey, settings, cancellationToken).ConfigureAwait(false);
        if (releaseFetched is not { Outcome: FetchOutcome.Success, Value.Release: { } release })
        {
            return Failed<IReadOnlyList<LabelReference>>(releaseFetched);
        }

        if (!MatchesMbid(release.Id, releaseMbid.ToString("D")))
        {
            return Outcome<IReadOnlyList<LabelReference>>(FetchOutcome.Malformed, releaseFetched.StatusCode);
        }

        if (release.LabelInfo is null)
        {
            return Outcome<IReadOnlyList<LabelReference>>(FetchOutcome.Malformed);
        }

        return Success(release.LabelReferences());
    }

    private IEnumerable<SearchHit> MapPage(QueryTarget target, SearchPage page)
    {
        return target switch
        {
            QueryTarget.Artist =>
                from artist in page.Artists
                let hit = artist.ToHit()
                where hit is not null
                select hit,
            QueryTarget.Album =>
                from releaseGroup in page.ReleaseGroups
                let hit = releaseGroup.ToHit()
                where hit is not null
                select hit,
            QueryTarget.Track =>
                from recording in page.Recordings
                let hit = recording.ToHit()
                where hit is not null
                select hit,
            _ => []
        };
    }

    private SearchHit[] ApplyResolvedArtistTypes(
        IReadOnlyList<QueryFilter> filters,
        IEnumerable<SearchHit> candidates)
    {
        var resolvedTypes = (
            from filter in filters
            from value in filter.RelationshipValues
            where value.ArtistMbid is not null && value.ArtistType is not null
            group value by value.ArtistMbid into values
            select new
            {
                Mbid = values.Key,
                ArtistType = values.Select(value => value.ArtistType).First()
            }).ToDictionary(value => value.Mbid, value => value.ArtistType, StringComparer.OrdinalIgnoreCase);

        return [..
            from hit in candidates
            let references = (
                from reference in hit.ArtistReferences
                let resolvedArtistType = reference.Mbid is not null &&
                    resolvedTypes.TryGetValue(reference.Mbid, out var resolvedType)
                        ? resolvedType
                        : null
                select reference with
                {
                    ArtistType = reference.ArtistType ?? resolvedArtistType
                }).ToArray()
            select hit with
            {
                ArtistReferences = references
            }];
    }

    private QueryResult DirectResult(
        QueryPlan plan,
        IReadOnlyList<SearchHit> hits,
        IEnumerable<string>? additionalWarnings = null)
    {
        var pageHits = plan.RequestedPage == 1 ? hits : [];
        return new QueryResult
        {
            Supported = true,
            Hits = pageHits,
            PageInfo = BoundedPageInfo(plan.RequestedPage, hits.Count, plan.MaxResults),
            Warnings = [.. plan.Warnings.Concat(additionalWarnings ?? []).Distinct()]
        };
    }

    private QueryResult RawCandidates(QueryPlan plan, IReadOnlyList<SearchHit> hits)
    {
        return new()
        {
            Supported = true,
            Hits = hits,
            PageInfo = new ProviderSearchPageInfo { RequestedPage = plan.RequestedPage, HasMore = false },
            Warnings = plan.Warnings
        };
    }

    private QueryResult Empty(QueryPlan plan)
    {
        return new()
        {
            Supported = true,
            Hits = [],
            PageInfo = new ProviderSearchPageInfo { RequestedPage = plan.RequestedPage, HasMore = false },
            Warnings = plan.Warnings
        };
    }

    private QueryResult Oversized(QueryPlan plan)
    {
        return new()
        {
            Supported = true,
            Hits = [],
            PageInfo = new ProviderSearchPageInfo { RequestedPage = plan.RequestedPage, HasMore = false },
            Warnings = [.. plan.Warnings, OversizedQueryWarning]
        };
    }

    private QueryResult Failure(QueryPlan plan, FetchOutcome outcome)
    {
        return new()
        {
            Supported = true,
            Hits = [],
            PageInfo = new ProviderSearchPageInfo { RequestedPage = plan.RequestedPage, HasMore = true },
            Warnings = [.. plan.Warnings, FailureWarning(outcome)]
        };
    }

    private string FailureWarning(FetchOutcome outcome)
    {
        var diagnostic = outcome switch
        {
            FetchOutcome.InvalidRequest => "musicbrainz rejected a generated query.",
            FetchOutcome.Timeout => "musicbrainz timed out.",
            FetchOutcome.Malformed => "musicbrainz returned an invalid response.",
            FetchOutcome.ResponseTooLarge => "musicbrainz returned a response above the 8 MiB limit.",
            _ => "musicbrainz is unavailable."
        };
        return diagnostic;
    }

    private ProviderSearchPageInfo BoundedPageInfo(int requestedPage, int count, int maxResults)
    {
        var totalPages = count <= 0 ? 0 : (count + maxResults - 1) / maxResults;
        return new ProviderSearchPageInfo { RequestedPage = requestedPage, HasMore = requestedPage < totalPages };
    }

    private ProviderSearchPageInfo DirectPageInfo(
        int requestedPage,
        SearchPage page,
        int rawHitCount,
        int filteredHitCount,
        int maxResults)
    {
        if (requestedPage == 1 && page.Offset == 0 && rawHitCount == page.Count)
        {
            return BoundedPageInfo(requestedPage, filteredHitCount, maxResults);
        }

        var providerCount = Math.Max(0L, page.Count);
        return new ProviderSearchPageInfo
        {
            RequestedPage = requestedPage,
            HasMore = Math.Max(0L, page.Offset) + rawHitCount < providerCount
        };
    }

    private int RawHitCount(QueryTarget target, SearchPage page)
    {
        return target switch
        {
            QueryTarget.Artist => page.Artists.Count,
            QueryTarget.Album => page.ReleaseGroups.Count + page.Releases.Count,
            QueryTarget.Track => page.Recordings.Count,
            _ => 0
        };
    }

    private FetchResult<VerifiedScan> ScanResult(VerifiedScan scan)
    {
        return new()
        {
            Outcome = FetchOutcome.Success,
            Value = scan
        };
    }

    private Request LinkedBrowseRequest(QueryTarget target, string artistMbid, int limit, int offset)
    {
        var endpoint = target switch
        {
            QueryTarget.Album => "release-group",
            QueryTarget.Track => "recording",
            _ => throw new InvalidOperationException("target")
        };
        var includes = target == QueryTarget.Track ? "artist-credits%2Bisrcs" : "artist-credits";
        return new Request
        {
            Target = target,
            RelativeTarget = string.Format(
                CultureInfo.InvariantCulture,
                "{0}?fmt=json&artist={1}&inc={2}&limit={3}&offset={4}",
                endpoint,
                Uri.EscapeDataString(artistMbid),
                includes,
                limit,
                offset),
            Fingerprint = string.Format(CultureInfo.InvariantCulture, "linked-browse:{0}:{1}", target, artistMbid),
            Limit = limit,
            Offset = offset
        };
    }

    private int PageOffset(int requestedPage, int maxResults)
    {
        return (int)Math.Min(Math.Max(0L, (long)requestedPage - 1) * maxResults, int.MaxValue);
    }

    private QueryResult EmptyWithWarnings(QueryPlan plan, IEnumerable<string> warnings)
    {
        return Empty(plan) with
        { Warnings = [.. plan.Warnings.Concat(warnings).Distinct()] };
    }

    private string NormalizeExactName(string value)
    {
        return string.Join(" ", value.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Normalize(NormalizationForm.FormKC);
    }

    private bool MatchesResolvedName(
        ArtistResolutionCandidate candidate,
        string value,
        CriteriaValueOperator operation)
    {
        if (operation != CriteriaValueOperator.ContainsString)
        {
            return string.Equals(NormalizeExactName(candidate.Name), value, StringComparison.OrdinalIgnoreCase);
        }

        return candidate.Aliases.Prepend(candidate.Name).Any(name => operation.MatchesString(
            [value],
            NormalizeExactName(name)));
    }

    private IReadOnlyList<ArtistDto> NormalizeArtists(
        IEnumerable<ArtistDto> artists)
    {
        return [.. artists
            .Where(artist => Guid.TryParse(artist.Id, out _))
            .Select(artist => artist with { Id = Guid.Parse(artist.Id!).ToString("D", CultureInfo.InvariantCulture) })
            .GroupBy(artist => artist.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())];
    }

    private string TerminalReason(QueryPlan plan, QueryResult result)
    {
        if (PlannedEmpty(plan))
        {
            if (plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty))
            {
                return "criteria-planned-empty";
            }

            return "no-positive-bounded-seed";
        }

        if (result.Warnings.Contains(OversizedQueryWarning))
        {
            return "request-target-too-large";
        }

        if (result.Warnings.Contains(ScanTruncationWarning))
        {
            return "upstream-result-limit-reached";
        }

        if (result.Warnings.Contains(FailureWarning(FetchOutcome.InvalidRequest)))
        {
            return "generated-request-invalid";
        }

        if (result.Warnings.Any(IsFailureWarning))
        {
            return "transport-failure";
        }

        if (result.PageInfo.HasMore)
        {
            return "page-settled-more-available";
        }

        return result.Hits.Count == 0 ? "no-matches" : "page-settled-source-exhausted";
    }

    private bool PlannedEmpty(QueryPlan plan)
    {
        return plan.Root is null or UnionPlanNode { Children.Count: 0 };
    }

    private bool IsFailureWarning(string warning)
    {
        return warning.Contains("could not", StringComparison.OrdinalIgnoreCase) ||
            warning.Contains("failed", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<QueryFilter>? ApplyResolvedRelationships(
        IReadOnlyList<QueryFilter> filters,
        IReadOnlyList<ResolvePlanNode> resolvers,
        IReadOnlyDictionary<int, QueryResult> outcomes)
    {
        var relationshipResolvers = resolvers
            .Where(resolver => resolver.RelationshipFilterIndex is not null)
            .GroupBy(resolver => resolver.RelationshipFilterIndex!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (relationshipResolvers.Count == 0)
        {
            return filters;
        }

        var resolvedFilters = filters.ToArray();
        foreach (var (filterIndex, filterResolvers) in relationshipResolvers)
        {
            var nodeFilterIndex = Array.FindIndex(resolvedFilters, filter => filter.SourceIndex == filterIndex);
            if (nodeFilterIndex < 0)
            {
                continue;
            }

            var filter = resolvedFilters[nodeFilterIndex];
            var values = new List<ArtistRelationshipValue>();
            for (var valueIndex = 0; valueIndex < filter.RelationshipValues.Count; valueIndex++)
            {
                var value = filter.RelationshipValues[valueIndex];
                var resolver = filterResolvers.SingleOrDefault(candidate =>
                    candidate.RelationshipValueIndex == valueIndex);
                if (resolver is null)
                {
                    values.Add(value);
                    continue;
                }

                var outcome = outcomes[resolver.Id];
                values.AddRange(outcome.Hits.Select(hit => value with
                {
                    ArtistMbid = hit.PrimaryMbid,
                    UseNameWhenMbidMissing = value.ArtistMbid is null && value.ArtistName is not null,
                    ArtistType = hit.ArtistType ?? value.ArtistType
                }));
            }

            if (values.Count == 0)
            {
                return null;
            }

            resolvedFilters[nodeFilterIndex] = filter with { RelationshipValues = values };
        }

        return resolvedFilters;
    }

    private RequestBuildResult Build(FetchPlanNode fetch, string fingerprint, int limit, int offset)
    {
        var query = fetch.OrdinaryTerm is not null
            ? EscapeLiteral(fetch.OrdinaryTerm)
            : BuildLucene(fetch.Filters.Where(filter => filter.RemoteApplied && !filter.ReleaseIndexApplied));
        if (query.Length == 0)
        {
            return new RequestBuildResult();
        }

        return BuildResult(fetch, Endpoint(fetch.Target), query, limit, offset, fingerprint, releaseIndex: false);
    }

    private RequestBuildResult BuildReleaseIndexSeed(
        FetchPlanNode fetch,
        string fingerprint,
        int limit,
        int offset)
    {
        var query = Join(
            fetch.Filters.Where(filter => IsReleaseIndexSeedFilter(fetch, filter)).Select(BuildReleaseIndexSeedClause),
            CombineMode.And);
        if (query.Length == 0)
        {
            return new RequestBuildResult();
        }

        return BuildResult(
            fetch,
            "release",
            query,
            limit,
            offset,
            fingerprint + ":release-index-seed",
            releaseIndex: true);
    }

    private bool IsReleaseIndexSeedFilter(FetchPlanNode fetch, QueryFilter filter)
    {
        return filter.RemoteApplied &&
            (filter.ReleaseIndexApplied || filter.Kind == QueryFilterKind.ExternalId ||
                fetch.Target == QueryTarget.Album && filter is { Kind: QueryFilterKind.Title, Negate: false });
    }

    private string BuildReleaseIndexSeedClause(QueryFilter filter)
    {
        return filter.Kind == QueryFilterKind.Title
            ? BuildClause(filter with { RemoteField = "releaseaccent" })
            : BuildClause(filter);
    }

    private RequestBuildResult BuildComposed(
        FetchPlanNode fetch,
        string fingerprint,
        IReadOnlyList<string> releaseGroupMbids)
    {
        var clauses = new List<string>
        {
            Group([.. releaseGroupMbids.Select(mbid => FieldPhrase("rgid", mbid))])
        };
        clauses.AddRange(
            fetch.Filters
                .Where(filter => filter.RemoteApplied && !filter.ReleaseIndexApplied)
                .Select(BuildClause)
                .Where(clause => clause.Length > 0));
        return BuildResult(
            fetch,
            Endpoint(fetch.Target),
            Join(clauses, CombineMode.And),
            releaseGroupMbids.Count,
            offset: 0,
            fingerprint + ":rg:" + HashValues(releaseGroupMbids),
            releaseIndex: false);
    }

    private RequestBuildResult BuildArtistComposition(
        FetchPlanNode fetch,
        string fingerprint,
        IReadOnlyList<string> includedArtistMbids,
        IReadOnlyList<string> excludedArtistMbids,
        int limit,
        int offset)
    {
        var clauses = new List<string>();
        if (includedArtistMbids.Count > 0)
        {
            clauses.Add(Group(includedArtistMbids.Select(mbid => FieldPhrase("arid", mbid))));
        }

        if (excludedArtistMbids.Count > 0)
        {
            clauses.Add("NOT (" + Group(excludedArtistMbids.Select(mbid => FieldPhrase("arid", mbid))) + ")");
        }

        clauses.AddRange(
            fetch.Filters
                .Where(filter => filter.RemoteApplied && !filter.ReleaseIndexApplied)
                .Select(BuildClause)
                .Where(clause => clause.Length > 0));
        if (clauses.Count == 0)
        {
            return new RequestBuildResult();
        }

        return BuildResult(
            fetch,
            "artist",
            Join(clauses, CombineMode.And),
            limit,
            offset,
            fingerprint + ":artist-composition:" +
            HashValues(includedArtistMbids.Concat(["!"]).Concat(excludedArtistMbids)),
            releaseIndex: false);
    }

    private ArtistResolutionRequest? BuildArtistResolution(ArtistComposition composition, int offset = 0)
    {
        if (composition.Resolution != ArtistCompositionResolution.ExactNameSearch ||
            string.IsNullOrWhiteSpace(composition.RelatedName))
        {
            return null;
        }

        var (endpoint, nameField, idField) = composition.RelatedEntity switch
        {
            ArtistCompositionEntity.Artist => ("artist", "artistaccent", "arid"),
            ArtistCompositionEntity.ReleaseGroup => ("release-group", "releasegroupaccent", "rgid"),
            ArtistCompositionEntity.Release => ("release", "releaseaccent", "reid"),
            ArtistCompositionEntity.Recording => ("recording", "recordingaccent", "rid"),
            ArtistCompositionEntity.Work => ("work", "workaccent", "wid"),
            _ => (string.Empty, string.Empty, string.Empty)
        };
        if (endpoint.Length == 0)
        {
            return null;
        }

        var query = composition.RelatedMbid is null
            ? composition.RelatedNameOperator == CriteriaValueOperator.ContainsString
                ? ArtistResolutionContainsClause(composition, nameField)
                : FieldPhrase(nameField, composition.RelatedName.Trim())
            : FieldPhrase(idField, composition.RelatedMbid);
        var target = string.Format(
            CultureInfo.InvariantCulture,
            "{0}?fmt=json&query={1}&limit={2}&offset={3}&dismax=false",
            endpoint,
            Uri.EscapeDataString(query),
            ArtistResolutionPageSize,
            Math.Max(0, offset));
        if (Encoding.UTF8.GetByteCount(target) > MaximumRequestTargetBytes)
        {
            return null;
        }

        return new ArtistResolutionRequest
        {
            Entity = composition.RelatedEntity,
            RelativeTarget = target,
            Fingerprint = "artist-resolution:" +
                HashValues(
                    [
                        composition.RelatedEntity.ToString(),
                        composition.RelatedMbid ?? string.Empty,
                        composition.RelatedName.Trim(),
                        composition.RelatedNameOperator.ToString(),
                        offset.ToString(CultureInfo.InvariantCulture)
                    ])
        };
    }

    private string ArtistResolutionContainsClause(ArtistComposition composition, string nameField)
    {
        var value = composition.RelatedName!.Trim();
        var primary = ContainsClause(nameField, value);
        return composition.RelatedEntity == ArtistCompositionEntity.Artist
            ? Group([primary, ContainsClause("alias", value)])
            : primary;
    }

    private RequestBuildResult BuildResult(
        FetchPlanNode fetch,
        string endpoint,
        string query,
        int limit,
        int offset,
        string fingerprint,
        bool releaseIndex)
    {
        var target = string.Format(
            CultureInfo.InvariantCulture,
            "{0}?fmt=json&query={1}&limit={2}&offset={3}&dismax={4}",
            endpoint,
            Uri.EscapeDataString(query),
            limit,
            offset,
            fetch.OrdinaryTerm is not null ? "true" : "false");
        if (Encoding.UTF8.GetByteCount(target) > MaximumRequestTargetBytes)
        {
            return new RequestBuildResult { TooLarge = true };
        }

        return new RequestBuildResult
        {
            Request = new Request
            {
                RelativeTarget = target,
                Target = fetch.Target,
                Fingerprint = fingerprint,
                Limit = limit,
                Offset = offset,
                ReleaseIndex = releaseIndex
            }
        };
    }

    private string HashValues(IEnumerable<string> values)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", values)));
        return BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty).ToLowerInvariant();
    }

    private string BuildClause(QueryFilter filter)
    {
        var clause = filter.Kind switch
        {
            QueryFilterKind.Established
                or QueryFilterKind.Dissolved
                or QueryFilterKind.BirthDate
                or QueryFilterKind.DeathDate
                or QueryFilterKind.PublishDate => BuildTemporalClause(filter),
            QueryFilterKind.Duration => BuildDurationClause(filter),
            QueryFilterKind.ExternalId => BuildIdentityClause(filter),
            QueryFilterKind.ArtistRelationship => BuildRelationshipClause(filter),
            QueryFilterKind.LabelRelationship => BuildLabelClause(filter),
            _ => BuildStringClause(filter)
        };
        return filter.Negate && clause.Length > 0 ? "NOT (" + clause + ")" : clause;
    }

    private string BuildLabelClause(QueryFilter filter)
    {
        var clauses = new List<string>();
        foreach (var value in filter.LabelValues)
        {
            var constraints = new List<string>();
            if (value.LabelMbid is not null)
            {
                constraints.Add(FieldPhrase("laid", value.LabelMbid));
            }

            if (value.LabelName is not null)
            {
                constraints.Add(FieldPhrase("label", value.LabelName));
            }

            if (constraints.Count > 0)
            {
                clauses.Add(constraints.Count == 1 ? constraints[0] : "(" + string.Join(" AND ", constraints) + ")");
            }
        }

        return Group(clauses, filter.CombineMode);
    }

    private string BuildStringClause(QueryFilter filter)
    {
        var field = filter.RemoteField;
        if (field is null)
        {
            return string.Empty;
        }

        var fields = filter.AlternateRemoteField is null ? new[] { field } : [field, filter.AlternateRemoteField];
        return Group([.. fields.Select(value => BuildStringFieldClause(filter, value))]);
    }

    private string BuildStringFieldClause(QueryFilter filter, string field)
    {
        if (filter.Operator == CriteriaValueOperator.IsNull)
        {
            return "NOT " + field + ":[* TO *]";
        }

        if (filter.Operator == CriteriaValueOperator.IsNotNull)
        {
            return field + ":[* TO *]";
        }

        var clauses = filter.StringValues.Select(value => filter.Operator switch
        {
            CriteriaValueOperator.ContainsString => ContainsClause(field, value),
            CriteriaValueOperator.StartsWithString => StartsWithClause(field, value),
            CriteriaValueOperator.EndsWithString => EndsWithClause(field, value),
            _ => FieldPhrase(field, value)
        }).ToArray();
        return Group(clauses, filter.CombineMode);
    }

    private string ContainsClause(string field, string value)
    {
        var wildcardClauses = value
            .SearchFragments()
            .Select(fragment => FieldWildcard(field, "*" + fragment + "*"))
            .ToArray();
        return Group([FieldPhrase(field, value), GroupAnd(wildcardClauses)]);
    }

    private string StartsWithClause(string field, string value)
    {
        var fragment = value.SearchFragments().FirstOrDefault();
        return fragment is null ? string.Empty : FieldWildcard(field, fragment + "*");
    }

    private string EndsWithClause(string field, string value)
    {
        var fragment = value.SearchFragments().LastOrDefault();
        return fragment is null ? string.Empty : FieldWildcard(field, "*" + fragment);
    }

    private string BuildIdentityClause(QueryFilter filter)
    {
        return Group(
            [.. filter.IdentityValues.Select(value => value.Value.Length == 0
                ? value.RemoteField + ":[* TO *]"
                : FieldPhrase(value.RemoteField, value.Value))],
            filter.CombineMode);
    }

    private string BuildRelationshipClause(QueryFilter filter)
    {
        var clauses = new List<string>();
        foreach (var value in filter.RelationshipValues)
        {
            var constraints = new List<string>();
            if (value.ArtistMbid is not null)
            {
                constraints.Add(FieldPhrase("arid", value.ArtistMbid));
            }

            if (value.ArtistName is not null && !value.UseNameWhenMbidMissing)
            {
                constraints.Add(value.ArtistNameOperator == CriteriaValueOperator.ContainsString
                    ? ContainsClause("artistname", value.ArtistName)
                    : FieldPhrase("artistname", value.ArtistName));
            }

            if (constraints.Count > 0)
            {
                clauses.Add(constraints.Count == 1 ? constraints[0] : "(" + string.Join(" AND ", constraints) + ")");
            }
        }

        return Group(clauses, filter.CombineMode);
    }

    private string BuildTemporalClause(QueryFilter filter)
    {
        if (filter.Kind == QueryFilterKind.Dissolved &&
            filter.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            const string existence = "(end:[* TO *] OR ended:true)";
            return filter.Operator == CriteriaValueOperator.IsNull ? "NOT " + existence : existence;
        }

        var field = filter.RemoteField;
        if (field is null)
        {
            return string.Empty;
        }

        if (filter.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return BuildStringClause(filter);
        }

        var clauses = filter.TemporalValues.Select(value => filter.Operator switch
        {
            CriteriaValueOperator.Equals or CriteriaValueOperator.In =>
                field + ":[" + FormatDate(value.PeriodStart()) + " TO " +
                FormatDate(value.PeriodEndExclusive().AddTicks(-1)) +
                "]",
            CriteriaValueOperator.GreaterThan =>
                field + ":[" + FormatDate(value.PeriodEndExclusive()) + " TO *]",
            CriteriaValueOperator.GreaterThanOrEqual =>
                field + ":[" + FormatDate(value.PeriodStart()) + " TO *]",
            CriteriaValueOperator.LessThan =>
                field + ":[* TO " + FormatDate(value.PeriodStart()) + "]",
            CriteriaValueOperator.LessThanOrEqual =>
                field + ":[* TO " + FormatDate(value.PeriodEndExclusive().AddTicks(-1)) + "]",
            _ => string.Empty
        }).Where(clause => clause.Length > 0).ToArray();
        return Group(clauses, filter.CombineMode);
    }

    private string BuildDurationClause(QueryFilter filter)
    {
        var field = filter.RemoteField;
        if (field is null)
        {
            return string.Empty;
        }

        if (filter.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return BuildStringClause(filter);
        }

        var clauses = filter.DurationMilliseconds.Select(value => filter.Operator switch
        {
            CriteriaValueOperator.Equals or CriteriaValueOperator.In =>
                field + ":" + value.ToString(CultureInfo.InvariantCulture),
            CriteriaValueOperator.GreaterThan =>
                field + ":[" + (value + 1).ToString(CultureInfo.InvariantCulture) + " TO *]",
            CriteriaValueOperator.GreaterThanOrEqual =>
                field + ":[" + value.ToString(CultureInfo.InvariantCulture) + " TO *]",
            CriteriaValueOperator.LessThan =>
                field + ":[* TO " + Math.Max(0, value - 1).ToString(CultureInfo.InvariantCulture) + "]",
            CriteriaValueOperator.LessThanOrEqual =>
                field + ":[* TO " + value.ToString(CultureInfo.InvariantCulture) + "]",
            _ => string.Empty
        }).Where(clause => clause.Length > 0).ToArray();
        return Group(clauses, filter.CombineMode);
    }

    private string FieldPhrase(string field, string value)
    {
        return field + ":\"" + EscapeLiteral(value) + "\"";
    }

    private string FieldWildcard(string field, string value) => field + ":" + value;

    private string BuildLucene(IEnumerable<QueryFilter> filters, CombineMode combineMode = CombineMode.And)
    {
        return Join(filters.Select(BuildClause), combineMode);
    }

    private string Group(IEnumerable<string> clauses, CombineMode combineMode = CombineMode.Or)
    {
        var values = clauses.Where(clause => clause.Length > 0).ToArray();
        return values.Length switch
        {
            0 => string.Empty,
            1 => values[0],
            _ => "(" + string.Join(combineMode == CombineMode.And ? " AND " : " OR ", values) + ")"
        };
    }

    private string GroupAnd(IEnumerable<string> clauses)
    {
        var values = clauses.Where(clause => clause.Length > 0).ToArray();
        return values.Length switch
        {
            0 => string.Empty,
            1 => values[0],
            _ => "(" + string.Join(" AND ", values) + ")"
        };
    }

    private string Join(IEnumerable<string> clauses, CombineMode combineMode)
    {
        var values = clauses.Where(clause => clause.Length > 0).ToArray();
        return string.Join(combineMode == CombineMode.And ? " AND " : " OR ", values);
    }

    private string EscapeLiteral(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '\\' or '+' or '-' or '&' or '|' or '!' or '(' or ')' or '{' or '}'
                or '[' or ']' or '^' or '"' or '~' or '*' or '?' or ':' or '/')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private string FormatDate(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private string Endpoint(QueryTarget target)
    {
        return target switch
        {
            QueryTarget.Artist => "artist",
            QueryTarget.Album => "release-group",
            QueryTarget.Track => "recording",
            _ => throw new InvalidOperationException("target")
        };
    }

    private IReadOnlyList<SearchHit> Filter(
        IReadOnlyList<QueryFilter> filters,
        IEnumerable<SearchHit> candidates)
    {
        var result = new List<SearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!Guid.TryParse(candidate.PrimaryMbid, out _) || !seen.Add(candidate.PrimaryMbid) ||
                filters.Where(filter => filter.VerifyLocally).Any(filter => !Matches(filter, candidate)))
            {
                continue;
            }

            result.Add(candidate);
        }

        return result;
    }

    private bool Matches(QueryFilter filter, SearchHit hit)
    {
        var matches = filter.Kind switch
        {
            QueryFilterKind.Title => MatchesString(filter, hit.Title),
            QueryFilterKind.SortTitle => MatchesString(filter, hit.SortTitle),
            QueryFilterKind.Overview => MatchesString(filter, hit.Disambiguation),
            QueryFilterKind.AlternateText => MatchesAlternateText(filter, hit.Title, hit.Aliases),
            QueryFilterKind.Established => MatchesTemporal(filter, hit.Established),
            QueryFilterKind.Dissolved => MatchesDissolved(filter, hit.Dissolved, hit.Ended),
            QueryFilterKind.BirthDate => MatchesTemporal(filter, hit.BirthDate),
            QueryFilterKind.DeathDate => MatchesTemporal(filter, hit.DeathDate),
            QueryFilterKind.PublishDate => MatchesTemporal(filter, hit.FirstReleaseDate),
            QueryFilterKind.Duration => MatchesDuration(filter, hit.Duration),
            QueryFilterKind.Language => MatchesLanguage(filter, hit.Languages),
            QueryFilterKind.ExternalId => MatchesIdentity(filter, hit),
            QueryFilterKind.ArtistRelationship => MatchesRelationship(filter, hit),
            QueryFilterKind.LabelRelationship => MatchesLabelRelationship(filter, hit),
            _ => true
        };
        return matches is null || (filter.Negate ? !matches.Value : matches.Value);
    }

    private bool? MatchesAlternateText(QueryFilter filter, string? title, IReadOnlyList<AliasDto>? aliases)
    {
        if (filter.IncludePrimaryTitle && MatchesString(filter, title))
        {
            return true;
        }

        return aliases?.Any(alias => MatchesString(filter, alias.Name));
    }

    private bool? MatchesLanguage(QueryFilter filter, IReadOnlyList<string>? languages)
    {
        return languages?.Any(language => MatchesString(filter, language));
    }

    private bool MatchesString(QueryFilter filter, string? candidate)
        => filter.Operator.MatchesString(filter.StringValues, candidate);

    private bool MatchesTemporal(QueryFilter filter, TemporalValue? candidate)
        => filter.Operator.MatchesTemporal(filter.TemporalValues, candidate);

    private bool MatchesDissolved(QueryFilter filter, TemporalValue? candidate, bool? ended)
    {
        if (filter.Operator == CriteriaValueOperator.IsNull)
        {
            return candidate is null && ended is not true;
        }

        if (filter.Operator == CriteriaValueOperator.IsNotNull)
        {
            return candidate is not null || ended is true;
        }

        return MatchesTemporal(filter, candidate);
    }

    private bool MatchesDuration(QueryFilter filter, TimeSpan? candidate)
        => filter.Operator.MatchesDuration(filter.DurationMilliseconds, candidate);

    private bool MatchesIdentity(QueryFilter filter, SearchHit hit)
    {
        return filter.IdentityValues.Any(expected => hit.ExternalIds.Any(actual =>
            string.Equals(actual.Source, expected.Source, StringComparison.OrdinalIgnoreCase) &&
            (expected.Value.Length == 0 ||
                string.Equals(actual.Value, expected.Value, StringComparison.OrdinalIgnoreCase))));
    }

    private bool MatchesRelationship(QueryFilter filter, SearchHit hit)
    {
        return filter.RelationshipValues.Any(expected => hit.ArtistReferences.Any(actual =>
            MatchesArtistIdentity(expected, actual) &&
            (expected.ArtistType is null ||
                string.Equals(actual.ArtistType, expected.ArtistType, StringComparison.OrdinalIgnoreCase)) &&
            (expected.ArtistName is null || expected.UseNameWhenMbidMissing || MatchesArtistName(expected, actual))));
    }

    private bool MatchesArtistIdentity(ArtistRelationshipValue expected, ArtistReference actual)
    {
        if (expected.ArtistMbid is null)
        {
            return true;
        }

        if (actual.Mbid is not null)
        {
            return string.Equals(actual.Mbid, expected.ArtistMbid, StringComparison.OrdinalIgnoreCase);
        }

        return expected.UseNameWhenMbidMissing &&
            expected.ArtistName is not null &&
            MatchesArtistName(expected, actual);
    }

    private bool MatchesArtistName(ArtistRelationshipValue expected, ArtistReference actual)
    {
        if (expected.ArtistNameOperator != CriteriaValueOperator.ContainsString)
        {
            return expected.ArtistNameOperator.MatchesString(
                [expected.ArtistName!],
                actual.CanonicalName ?? actual.CreditName);
        }

        return expected.ArtistNameOperator.MatchesString([expected.ArtistName!], actual.CanonicalName) ||
            expected.ArtistNameOperator.MatchesString([expected.ArtistName!], actual.CreditName);
    }

    private bool? MatchesLabelRelationship(QueryFilter filter, SearchHit hit)
    {
        return hit.LabelReferences?.Any(actual => filter.LabelValues.Any(expected =>
            (expected.LabelMbid is null ||
                string.Equals(actual.Mbid, expected.LabelMbid, StringComparison.OrdinalIgnoreCase)) &&
            (expected.LabelName is null ||
                string.Equals(actual.Name, expected.LabelName, StringComparison.OrdinalIgnoreCase))));
    }

    private EntityKind EntityKindFor(QueryTarget target)
    {
        return target switch
        {
            QueryTarget.Artist => EntityKind.Artist,
            QueryTarget.Album => EntityKind.ReleaseGroup,
            QueryTarget.Track => EntityKind.Recording,
            _ => throw new InvalidOperationException("Unsupported MusicBrainz residual target.")
        };
    }

    private FetchResult<T> Success<T>(T value)
        where T : class
    {
        return new() { Outcome = FetchOutcome.Success, Value = value };
    }

    private FetchResult<ResidualEntityData> ReleaseDuration(ReleaseDto release)
    {
        var tracks = (release.Media ?? [])
            .Where(medium => medium is not null)
            .SelectMany(medium => medium!.Tracks ?? [])
            .Where(track => track is not null)
            .ToList();
        if (tracks.Count == 0)
        {
            return Success(new ResidualEntityData());
        }

        var lengths = tracks.Select(track => track!.Length ?? track.Recording?.Length).ToList();
        if (lengths.Any(length => length is null or < 0))
        {
            return Outcome<ResidualEntityData>(FetchOutcome.Malformed);
        }

        return Success(new ResidualEntityData
        { Duration = TimeSpan.FromMilliseconds(lengths.Sum(length => length!.Value)) });
    }

    private FetchResult<TOut> Failed<TOut>(FetchResult<EntityResponseDto> fetched)
        where TOut : class
    {
        return new() { Outcome = fetched.Outcome, StatusCode = fetched.StatusCode };
    }

    private FetchResult<T> Outcome<T>(FetchOutcome outcome, int? statusCode = null)
        where T : class
    {
        return new() { Outcome = outcome, StatusCode = statusCode };
    }

    private bool MatchesMbid(string? actual, string expected)
    {
        return Guid.TryParse(actual, out var actualMbid) &&
            Guid.TryParse(expected, out var expectedMbid) &&
            actualMbid == expectedMbid;
    }

    private Task<QueryResult> ExecuteCoreAsync(
        QueryPlan plan,
        CancellationToken cancellationToken)
    {
        return plan.Root is null
            ? Task.FromResult(Empty(plan))
            : ExecuteNodeAsync(plan, plan.Root, new Dictionary<int, QueryResult>(), cancellationToken);
    }

    private async Task<QueryResult> ExecuteNodeAsync(
        QueryPlan plan,
        PlanNode node,
        Dictionary<int, QueryResult> outcomes,
        CancellationToken cancellationToken)
    {
        if (outcomes.TryGetValue(node.Id, out var memoized))
        {
            return memoized;
        }

        var stopwatch = Stopwatch.StartNew();
        var previousNodeId = _currentNodeId.Value;
        var previousStageNodeId = _currentStageNodeId.Value;
        _currentNodeId.Value = node.Id;
        _currentStageNodeId.Value = node is FilterPlanNode ? node.Id : null;
        QueryResult result;
        try
        {
            result = node switch
            {
                FetchPlanNode fetch => await ExecuteFetchNodeAsync(
                    plan,
                    fetch,
                    outcomes,
                    filter: null,
                    maximumRows: null,
                    cancellationToken).ConfigureAwait(false),
                FilterPlanNode filter => await ExecuteFilterNodeAsync(plan, filter, outcomes, cancellationToken)
                    .ConfigureAwait(false),
                UnionPlanNode union => await ExecuteUnionNodeAsync(plan, union, outcomes, cancellationToken)
                    .ConfigureAwait(false),
                IntersectPlanNode intersect => await ExecuteIntersectNodeAsync(
                    plan,
                    intersect,
                    outcomes,
                    cancellationToken).ConfigureAwait(false),
                ResolvePlanNode resolve => await ExecuteResolveNodeAsync(plan, resolve, cancellationToken)
                    .ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(node))
            };
        }
        finally
        {
            _currentNodeId.Value = previousNodeId;
            _currentStageNodeId.Value = previousStageNodeId;
        }

        outcomes[node.Id] = result;
        _currentTrace.Value?.RecordNode(node, result, stopwatch.ElapsedMilliseconds);
        return result;
    }

    private async Task<QueryResult> ExecuteFilterNodeAsync(
        QueryPlan plan,
        FilterPlanNode filter,
        Dictionary<int, QueryResult> outcomes,
        CancellationToken cancellationToken)
    {
        if (filter.Child is FetchPlanNode { AccessMode: AccessMode.Search } search && filter.Exclusions.Count == 0)
        {
            return await ExecuteFilteredFetchNodeAsync(
                plan,
                search,
                filter,
                outcomes,
                maximumRows: null,
                cancellationToken).ConfigureAwait(false);
        }

        if (filter.Child is FetchPlanNode { AccessMode: AccessMode.Search } excludedSearch)
        {
            return await ExecuteSearchWithExclusionsAsync(
                plan,
                excludedSearch,
                filter,
                outcomes,
                cancellationToken).ConfigureAwait(false);
        }

        var child = filter.Child is FetchPlanNode fetch
            ? await ExecuteFilteredFetchNodeAsync(
                plan,
                fetch,
                filter,
                outcomes,
                maximumRows: (int)Math.Min((long)plan.RequestedPage * plan.MaxResults + 1, int.MaxValue),
                cancellationToken).ConfigureAwait(false)
            : await ExecuteNodeAsync(plan, filter.Child, outcomes, cancellationToken).ConfigureAwait(false);
        if (!child.Supported || child.Hits.Count == 0)
        {
            return child;
        }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>(child.Warnings);
        foreach (var exclusion in filter.Exclusions)
        {
            var outcome = await ExecuteNodeAsync(plan, exclusion, outcomes, cancellationToken).ConfigureAwait(false);
            warnings.AddRange(outcome.Warnings);
            excluded.UnionWith(outcome.Hits.Select(IdentityKey));
        }

        var candidates = child.Hits.Where(hit => !excluded.Contains(IdentityKey(hit))).ToList();
        var resolvers = filter.Child.Flatten().OfType<ResolvePlanNode>().ToArray();
        var localFilters = ApplyResolvedRelationships(filter.Filters, resolvers, outcomes);
        if (localFilters is null)
        {
            return EmptyWithWarnings(plan, warnings);
        }

        var locallyFiltered = FilterCandidates(localFilters, candidates, "filter");
        return await ExecuteBoundedCandidatesAsync(
            plan,
            filter.Residuals,
            locallyFiltered,
            [.. warnings.Distinct()],
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<QueryResult> ExecuteFilteredFetchNodeAsync(
        QueryPlan plan,
        FetchPlanNode fetch,
        FilterPlanNode filter,
        Dictionary<int, QueryResult> outcomes,
        int? maximumRows,
        CancellationToken cancellationToken)
    {
        if (outcomes.TryGetValue(fetch.Id, out var memoized))
        {
            return memoized;
        }

        var stopwatch = Stopwatch.StartNew();
        var previousNodeId = _currentNodeId.Value;
        _currentNodeId.Value = fetch.Id;
        QueryResult result;
        try
        {
            result = await ExecuteFetchNodeAsync(
                plan,
                fetch,
                outcomes,
                filter,
                maximumRows,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _currentNodeId.Value = previousNodeId;
        }

        outcomes[fetch.Id] = result;
        _currentTrace.Value?.RecordNode(fetch, result, stopwatch.ElapsedMilliseconds);
        return result;
    }

    private async Task<QueryResult> ExecuteSearchWithExclusionsAsync(
        QueryPlan plan,
        FetchPlanNode search,
        FilterPlanNode filter,
        Dictionary<int, QueryResult> outcomes,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var excludedMbids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var exclusion in filter.Exclusions)
        {
            var result = await ExecuteNodeAsync(plan, exclusion, outcomes, cancellationToken).ConfigureAwait(false);
            warnings.AddRange(result.Warnings);
            excludedMbids.UnionWith(result.Hits.Select(hit => hit.PrimaryMbid));
        }

        if (excludedMbids.Count == 0)
        {
            var result = await ExecuteFilteredFetchNodeAsync(
                plan,
                search,
                filter,
                outcomes,
                maximumRows: null,
                cancellationToken).ConfigureAwait(false);
            return result with
            { Warnings = [.. result.Warnings.Concat(warnings).Distinct()] };
        }

        QueryResult? anchor = null;
        if (search.AnchorSource is not null)
        {
            anchor = await ExecuteNodeAsync(plan, search.AnchorSource, outcomes, cancellationToken)
                .ConfigureAwait(false);
            if (anchor.Hits.Count == 0)
            {
                _currentTrace.Value?.RecordNodeSkipped(search, "required anchor returned no ids");
                return EmptyWithWarnings(plan, anchor.Warnings.Concat(warnings));
            }
        }

        var query = new ArtistCompositionQuery
        {
            IncludedArtistMbids = anchor?.Hits
                .Select(hit => hit.PrimaryMbid)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(mbid => mbid, StringComparer.Ordinal)
                .ToArray() ??
                [],
            ExcludedArtistMbids = [.. excludedMbids.OrderBy(mbid => mbid, StringComparer.Ordinal)],
            Warnings = [.. warnings.Concat(anchor?.Warnings ?? []).Distinct()]
        };
        var stopwatch = Stopwatch.StartNew();
        var previousNodeId = _currentNodeId.Value;
        _currentNodeId.Value = search.Id;
        QueryResult searched;
        try
        {
            searched = await ExecuteVerifiedAsync(plan, search, filter, cancellationToken, query)
                .ConfigureAwait(false);
        }
        finally
        {
            _currentNodeId.Value = previousNodeId;
        }

        outcomes[search.Id] = searched;
        _currentTrace.Value?.RecordNode(search, searched, stopwatch.ElapsedMilliseconds);
        return searched;
    }

    private async Task<QueryResult> ExecuteFetchNodeAsync(
        QueryPlan plan,
        FetchPlanNode fetch,
        Dictionary<int, QueryResult> outcomes,
        FilterPlanNode? filter,
        int? maximumRows,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<int, QueryResult>();
        foreach (var resolver in fetch.Resolvers)
        {
            var outcome = await ExecuteNodeAsync(plan, resolver, outcomes, cancellationToken).ConfigureAwait(false);
            resolved[resolver.Id] = outcome;
            if (!outcome.Supported || outcome is { Hits.Count: 0, PageInfo.HasMore: true })
            {
                return outcome;
            }
        }

        QueryResult? anchor = null;
        if (fetch.AnchorSource is not null)
        {
            anchor = await ExecuteNodeAsync(plan, fetch.AnchorSource, outcomes, cancellationToken)
                .ConfigureAwait(false);
            if (!anchor.Supported)
            {
                return anchor;
            }
        }

        if (fetch.Precondition is { Kind: PlanPreconditionKind.NodeNonEmpty, NodeId: { } requiredNodeId } &&
            (resolved.TryGetValue(requiredNodeId, out var required) ||
                outcomes.TryGetValue(requiredNodeId, out required)) &&
            required.Hits.Count == 0)
        {
            _currentTrace.Value?.RecordNodeSkipped(fetch, "required dependency returned no ids");
            return EmptyWithWarnings(plan, required.Warnings);
        }

        var fetchFilters = ApplyResolvedRelationships(fetch.Filters, fetch.Resolvers, resolved);
        var localFilters = filter is null ? [] : ApplyResolvedRelationships(filter.Filters, fetch.Resolvers, resolved);
        if (fetchFilters is null || localFilters is null)
        {
            _currentTrace.Value?.RecordNodeSkipped(fetch, "required resolver returned no ids");
            return EmptyWithWarnings(plan, resolved.Values.SelectMany(outcome => outcome.Warnings));
        }

        var executionFetch = fetch with { Filters = fetchFilters };
        var executionFilter = filter is null ? null : filter with { Filters = localFilters };
        var executionPlan = plan with
        {
            Warnings = [.. plan.Warnings
                .Concat(resolved.Values.SelectMany(outcome => outcome.Warnings))
                .Distinct()]
        };
        return executionFetch.AccessMode switch
        {
            AccessMode.Search when anchor is not null => await ExecuteAnchoredSearchAsync(
                executionPlan,
                executionFetch,
                executionFilter,
                anchor,
                cancellationToken).ConfigureAwait(false),
            AccessMode.Search when executionFilter is not null || executionFetch.UsesReleaseIndexComposition =>
                await ExecuteVerifiedAsync(executionPlan, executionFetch, executionFilter, cancellationToken)
                    .ConfigureAwait(false),
            AccessMode.Search => await ExecutePageAsync(executionPlan, executionFetch, cancellationToken)
                .ConfigureAwait(false),
            AccessMode.Lookup => await ExecuteEntityAsync(
                executionPlan,
                executionFetch,
                executionFilter,
                cancellationToken).ConfigureAwait(false),
            AccessMode.IsrcLookup => await ExecuteIsrcAsync(
                executionPlan,
                executionFetch,
                executionFilter,
                cancellationToken).ConfigureAwait(false),
            AccessMode.Browse => await ExecuteBrowseFetchAsync(
                executionPlan,
                executionFetch,
                executionFilter,
                resolved,
                maximumRows,
                cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(fetch))
        };
    }

    private Task<QueryResult> ExecuteAnchoredSearchAsync(
        QueryPlan plan,
        FetchPlanNode fetch,
        FilterPlanNode? filter,
        QueryResult anchor,
        CancellationToken cancellationToken)
    {
        var query = new ArtistCompositionQuery
        {
            IncludedArtistMbids = [.. anchor.Hits
                .Select(hit => hit.PrimaryMbid)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(mbid => mbid, StringComparer.Ordinal)],
            ExcludedArtistMbids = [],
            Warnings = anchor.Warnings
        };
        return filter is not null
            ? ExecuteVerifiedAsync(plan, fetch, filter, cancellationToken, query)
            : ExecuteArtistCompositionPageAsync(plan, fetch, query, cancellationToken);
    }

    private async Task<QueryResult> ExecuteResolveNodeAsync(
        QueryPlan plan,
        ResolvePlanNode resolve,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveArtistCompositionAsync(plan, resolve.Composition, cancellationToken)
            .ConfigureAwait(false);
        if (resolution.Outcome != FetchOutcome.Success)
        {
            return Failure(plan, resolution.Outcome);
        }

        var hits = resolution.Artists.Select(artist => new SearchHit
        {
            Target = QueryTarget.Artist,
            PrimaryMbid = artist.Mbid,
            Title = resolve.Composition.RelatedName,
            ArtistType = artist.ArtistType
        }).ToArray();
        return RawCandidates(plan, hits) with
        { Warnings = [.. plan.Warnings.Concat(resolution.Warnings).Distinct()] };
    }

    private async Task<QueryResult> ExecuteBrowseFetchAsync(
        QueryPlan plan,
        FetchPlanNode fetch,
        FilterPlanNode? filter,
        IReadOnlyDictionary<int, QueryResult> resolved,
        int? maximumRows,
        CancellationToken cancellationToken)
    {
        if (fetch.BrowseComposition is null)
        {
            return await ExecuteLinkedBrowseFetchAsync(plan, fetch, filter, resolved, maximumRows, cancellationToken)
                .ConfigureAwait(false);
        }

        var composition = fetch.BrowseComposition;
        var resolvedMbids = fetch.Resolvers
            .SelectMany(resolver => resolved[resolver.Id].Hits)
            .Select(hit => hit.PrimaryMbid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (resolvedMbids.Length == 0)
        {
            return Empty(plan);
        }

        var artists = new List<ArtistDto>();
        foreach (var mbid in resolvedMbids)
        {
            var expansion = await ExpandArtistCompositionAsync(plan, composition, mbid, cancellationToken)
                .ConfigureAwait(false);
            if (expansion.Outcome == FetchOutcome.NotFound)
            {
                continue;
            }

            if (expansion.Outcome != FetchOutcome.Success)
            {
                return Failure(plan, expansion.Outcome);
            }

            artists.AddRange(expansion.Artists);
        }

        var hits = (from artist in NormalizeArtists(artists)
                    let hit = artist.ToHit()
                    where hit is not null
                    select hit).ToArray();
        return new QueryResult
        {
            Supported = true,
            Hits = hits,
            PageInfo = new ProviderSearchPageInfo { RequestedPage = plan.RequestedPage, HasMore = false },
            Warnings = plan.Warnings
        };
    }

    private async Task<QueryResult> ExecuteLinkedBrowseFetchAsync(
        QueryPlan plan,
        FetchPlanNode fetch,
        FilterPlanNode? filter,
        IReadOnlyDictionary<int, QueryResult> resolved,
        int? maximumRows,
        CancellationToken cancellationToken)
    {
        var artistMbids = new List<string>();
        if (fetch.DirectValue is not null)
        {
            artistMbids.Add(fetch.DirectValue);
        }

        artistMbids.AddRange(
            fetch.Resolvers
                .SelectMany(resolver => resolved[resolver.Id].Hits)
                .Select(hit => hit.PrimaryMbid));
        var hits = new List<SearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var survivorCount = 0;
        foreach (var artistMbid in artistMbids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var offset = 0;
            var count = int.MaxValue;
            while (offset < count)
            {
                var request = LinkedBrowseRequest(fetch.Target, artistMbid, BrowsePageSize, offset);
                var fetched = await SearchPageAsync(request, plan.Settings, "browse", cancellationToken)
                    .ConfigureAwait(false);
                if (fetched is not { Outcome: FetchOutcome.Success, Value: { } page })
                {
                    return Failure(plan, fetched.Outcome);
                }

                count = Math.Max(0, page.Count);
                var rawHitCount = RawHitCount(fetch.Target, page);
                var filters = filter?.Filters ?? fetch.Filters;
                var pageHits = ApplyResolvedArtistTypes(filters, MapPage(fetch.Target, page));
                foreach (var hit in pageHits)
                {
                    if (seen.Add(IdentityKey(hit)))
                    {
                        hits.Add(hit);
                        if (filters.All(filterCandidate => !filterCandidate.VerifyLocally ||
                            Matches(filterCandidate, hit)))
                        {
                            survivorCount++;
                        }

                        if (maximumRows is { } goal && survivorCount >= goal)
                        {
                            return RawCandidates(plan, hits);
                        }
                    }
                }

                if (rawHitCount == 0)
                {
                    break;
                }

                offset += rawHitCount;
            }
        }

        return RawCandidates(plan, hits);
    }

    private async Task<QueryResult> ExecuteUnionNodeAsync(
        QueryPlan plan,
        UnionPlanNode union,
        Dictionary<int, QueryResult> outcomes,
        CancellationToken cancellationToken)
    {
        var results = new List<QueryResult>();
        foreach (var child in union.Children)
        {
            results.Add(await ExecuteNodeAsync(plan, child, outcomes, cancellationToken).ConfigureAwait(false));
        }

        return MergeNodeResults(plan, results, intersect: false);
    }

    private async Task<QueryResult> ExecuteIntersectNodeAsync(
        QueryPlan plan,
        IntersectPlanNode intersect,
        Dictionary<int, QueryResult> outcomes,
        CancellationToken cancellationToken)
    {
        var results = new List<QueryResult>();
        for (var index = 0; index < intersect.Children.Count; index++)
        {
            var result = await ExecuteNodeAsync(
                plan,
                intersect.Children[index],
                outcomes,
                cancellationToken).ConfigureAwait(false);
            results.Add(result);
            if (result.Hits.Count > 0 || result.PageInfo.HasMore)
            {
                continue;
            }

            foreach (var skipped in intersect.Children.Skip(index + 1).SelectMany(child => child.Flatten()))
            {
                _currentTrace.Value?.RecordNodeSkipped(skipped, "intersection already empty");
            }

            break;
        }

        return MergeNodeResults(plan, results, intersect: true);
    }

    private QueryResult MergeNodeResults(QueryPlan plan, IReadOnlyList<QueryResult> results, bool intersect)
    {
        if (results.Count == 0)
        {
            return Empty(plan);
        }

        var hits = results[0].Hits.ToList();
        if (intersect)
        {
            foreach (var identities in results.Skip(1).Select(result =>
                result.Hits.Select(IdentityKey).ToHashSet(StringComparer.OrdinalIgnoreCase)))
            {
                hits.RemoveAll(hit => !identities.Contains(IdentityKey(hit)));
            }
        }
        else
        {
            var seen = hits.Select(IdentityKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var hit in results.Skip(1).SelectMany(result => result.Hits))
            {
                if (seen.Add(IdentityKey(hit)))
                {
                    hits.Add(hit);
                }
            }
        }

        return new QueryResult
        {
            Supported = results.All(result => result.Supported),
            Hits = hits,
            PageInfo = new ProviderSearchPageInfo
            {
                RequestedPage = plan.RequestedPage,
                HasMore = results.Any(result => result.PageInfo.HasMore)
            },
            Warnings = [.. results.SelectMany(result => result.Warnings).Distinct()]
        };
    }

    private string IdentityKey(SearchHit hit) => hit.Target + ":" + hit.PrimaryMbid;

    private async Task<QueryResult> ExecutePageAsync(
        QueryPlan plan,
        FetchPlanNode fetch,
        CancellationToken cancellationToken)
    {
        var offset = PageOffset(plan.RequestedPage, plan.MaxResults);
        var built = Build(fetch, plan.Fingerprint, plan.MaxResults, offset);
        if (built.TooLarge)
        {
            TraceStageSkipped("seed", "request target exceeds the maximum size");
            return Oversized(plan);
        }

        if (built.Request is null)
        {
            TraceStageSkipped("seed", "request builder produced no request");
            return Empty(plan);
        }

        var fetched = await SearchPageAsync(built.Request, plan.Settings, "seed", cancellationToken)
            .ConfigureAwait(false);
        if (fetched is not { Outcome: FetchOutcome.Success, Value: { } page })
        {
            return Failure(plan, fetched.Outcome);
        }

        var rawHitCount = RawHitCount(fetch.Target, page);
        var hits = FilterCandidates([], MapPage(fetch.Target, page), "seed");
        return new QueryResult
        {
            Supported = true,
            Hits = hits,
            PageInfo = DirectPageInfo(plan.RequestedPage, page, rawHitCount, hits.Count, plan.MaxResults),
            Warnings = plan.Warnings
        };
    }

    private async Task<QueryResult> ExecuteVerifiedAsync(
        QueryPlan plan,
        FetchPlanNode fetch,
        FilterPlanNode? filter,
        CancellationToken cancellationToken,
        ArtistCompositionQuery? artistCompositionQuery = null)
    {
        var preflight = artistCompositionQuery is not null
            ? BuildArtistComposition(
                fetch,
                plan.Fingerprint,
                artistCompositionQuery.IncludedArtistMbids,
                artistCompositionQuery.ExcludedArtistMbids,
                ScanBatchSize,
                offset: 0)
            : fetch.UsesReleaseIndexComposition
                ? BuildReleaseIndexSeed(fetch, plan.Fingerprint, ScanBatchSize, offset: 0)
                : Build(fetch, plan.Fingerprint, ScanBatchSize, offset: 0);
        if (preflight.TooLarge)
        {
            TraceStageSkipped(
                artistCompositionQuery is null ? "seed" : "composed",
                "request target exceeds the maximum size");
            return Oversized(plan);
        }

        if (preflight.Request is null)
        {
            TraceStageSkipped(
                artistCompositionQuery is null ? "seed" : "composed",
                "request builder produced no request");
            return Empty(plan);
        }

        var diagnostics = new VerifiedScanDiagnostics();
        var page = Math.Max(1, plan.RequestedPage);
        var remainingSkip = ((long)page - 1) * plan.MaxResults;
        var hits = new List<SearchHit>();
        await foreach (var hit in VerifiedHitsAsync(
            plan,
            fetch,
            filter,
            diagnostics,
            preflight.Request.Fingerprint,
            artistCompositionQuery,
            cancellationToken)
            .ConfigureAwait(false))
        {
            if (remainingSkip > 0)
            {
                remainingSkip--;
                continue;
            }

            hits.Add(hit);
            if (hits.Count > plan.MaxResults)
            {
                break;
            }
        }

        var faulted = diagnostics.Outcome != FetchOutcome.Success;
        var hasMore = faulted || hits.Count > plan.MaxResults;
        if (hits.Count > plan.MaxResults)
        {
            hits.RemoveAt(plan.MaxResults);
        }

        return new QueryResult
        {
            Supported = true,
            Hits = hits,
            PageInfo = new ProviderSearchPageInfo { RequestedPage = page, HasMore = hasMore },
            Warnings = [.. plan.Warnings
                .Concat(faulted ? [FailureWarning(diagnostics.Outcome)] : [])
                .Concat(diagnostics.Truncated ? [ScanTruncationWarning] : [])
                .Concat(diagnostics.Warnings)
                .Concat(artistCompositionQuery?.Warnings ?? [])
                .Distinct()]
        };
    }

    private async IAsyncEnumerable<SearchHit> VerifiedHitsAsync(
        QueryPlan plan,
        FetchPlanNode fetch,
        FilterPlanNode? filter,
        VerifiedScanDiagnostics diagnostics,
        string scanFingerprint,
        ArtistCompositionQuery? artistCompositionQuery,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var yielded = 0;
        while (true)
        {
            var fetched = await cache.GetVerifiedScanAsync(
                scanFingerprint,
                scan =>
                {
                    var snapshot = scan.Read();
                    return snapshot.Exhausted || snapshot.HitCount > yielded;
                },
                (existing, sharedToken) => ExtendScanAsync(
                    plan,
                    fetch,
                    filter,
                    existing,
                    artistCompositionQuery,
                    sharedToken),
                cancellationToken).ConfigureAwait(false);
            if (fetched is not { Outcome: FetchOutcome.Success, Value: { } scan })
            {
                diagnostics.Outcome = fetched.Outcome;
                yield break;
            }

            var snapshot = scan.Read();
            TraceScanSnapshot(artistCompositionQuery is null ? "seed" : "composed", scan);
            diagnostics.Truncated = snapshot.Truncated;
            diagnostics.Warnings = snapshot.Warnings;
            while (yielded < snapshot.HitCount)
            {
                yield return scan.HitAt(yielded++);
            }

            if (snapshot.Exhausted)
            {
                yield break;
            }
        }
    }

    private async Task<FetchResult<VerifiedScan>> ExtendScanAsync(
        QueryPlan plan,
        FetchPlanNode fetch,
        FilterPlanNode? filter,
        VerifiedScan? existing,
        ArtistCompositionQuery? artistCompositionQuery,
        CancellationToken cancellationToken)
    {
        var scan = existing ?? new VerifiedScan();
        var snapshot = scan.Read();
        if (snapshot.Exhausted)
        {
            TraceStageSkipped(
                artistCompositionQuery is null ? "seed" : "composed",
                "verified scan is already exhausted");
            return ScanResult(scan);
        }

        if (scan.TryPeekPending(out var pending) && pending is not null)
        {
            var evaluated = await EvaluateDeferredAsync(
                filter?.Residuals ?? [],
                plan.Settings,
                pending,
                cancellationToken).ConfigureAwait(false);
            scan.CompletePending(evaluated.Accepted, evaluated.Candidate, evaluated.Warnings);
            TraceDeferredDelta(pending.PrimaryMbid, evaluated.Accepted, scan);
            return ScanResult(scan);
        }

        if (snapshot.SourceExhausted)
        {
            TraceStageSkipped(artistCompositionQuery is null ? "seed" : "composed", "source scan is exhausted");
            return ScanResult(scan);
        }

        return fetch.UsesReleaseIndexComposition
            ? await ExtendReleaseIndexScanAsync(plan, fetch, filter, scan, snapshot.NextOffset, cancellationToken)
                .ConfigureAwait(false)
            : await ExtendStandardScanAsync(
                plan,
                fetch,
                filter,
                scan,
                snapshot.NextOffset,
                artistCompositionQuery,
                cancellationToken)
                .ConfigureAwait(false);
    }

    private async Task<FetchResult<VerifiedScan>> ExtendStandardScanAsync(
        QueryPlan plan,
        FetchPlanNode fetch,
        FilterPlanNode? filter,
        VerifiedScan scan,
        int nextOffset,
        ArtistCompositionQuery? artistCompositionQuery,
        CancellationToken cancellationToken)
    {
        var role = artistCompositionQuery is null ? "seed" : "composed";
        var examinedBefore = nextOffset;
        var built = artistCompositionQuery is null
            ? Build(fetch, plan.Fingerprint, ScanBatchSize, nextOffset)
            : BuildArtistComposition(
                fetch,
                plan.Fingerprint,
                artistCompositionQuery.IncludedArtistMbids,
                artistCompositionQuery.ExcludedArtistMbids,
                ScanBatchSize,
                nextOffset);
        if (built.TooLarge || built.Request is null)
        {
            TraceStageSkipped(
                role,
                built.TooLarge ? "request target exceeds the maximum size" : "request builder produced no request");
            return new FetchResult<VerifiedScan> { Outcome = FetchOutcome.InvalidRequest };
        }

        var fetched = await SearchPageAsync(
            built.Request,
            plan.Settings,
            role,
            cancellationToken).ConfigureAwait(false);
        if (fetched is not { Outcome: FetchOutcome.Success, Value: { } page })
        {
            return new FetchResult<VerifiedScan> { Outcome = fetched.Outcome, StatusCode = fetched.StatusCode };
        }

        var rawHitCount = RawHitCount(fetch.Target, page);
        var providerHits = MapPage(fetch.Target, page).ToArray();
        nextOffset += rawHitCount;
        var sourceExhausted = rawHitCount == 0 || rawHitCount < ScanBatchSize || nextOffset >= page.Count;
        var filtered = FilterCandidates(filter?.Filters ?? [], providerHits, role);
        AppendCandidates(scan, filter?.Residuals ?? [], filtered, nextOffset, sourceExhausted, truncated: false, role);
        TraceScanProgress(role, examinedBefore, rawHitCount, providerHits.Length, filtered.Count, scan);
        return ScanResult(scan);
    }

    private async Task<FetchResult<VerifiedScan>> ExtendReleaseIndexScanAsync(
        QueryPlan plan,
        FetchPlanNode fetch,
        FilterPlanNode? filter,
        VerifiedScan scan,
        int nextOffset,
        CancellationToken cancellationToken)
    {
        var examinedBefore = nextOffset;
        var built = BuildReleaseIndexSeed(fetch, plan.Fingerprint, ScanBatchSize, nextOffset);
        if (built.TooLarge || built.Request is null)
        {
            TraceStageSkipped(
                "seed",
                built.TooLarge ? "request target exceeds the maximum size" : "request builder produced no request");
            return new FetchResult<VerifiedScan> { Outcome = FetchOutcome.InvalidRequest };
        }

        var fetched = await SearchPageAsync(built.Request, plan.Settings, "seed", cancellationToken)
            .ConfigureAwait(false);
        if (fetched is not { Outcome: FetchOutcome.Success, Value: { } page })
        {
            return new FetchResult<VerifiedScan> { Outcome = fetched.Outcome, StatusCode = fetched.StatusCode };
        }

        var order = new List<string>();
        var references = new Dictionary<string, List<LabelReference>>(StringComparer.OrdinalIgnoreCase);
        var languages = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var labelDataComplete = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var languageDataComplete = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var release in page.Releases)
        {
            if (release is null || !Guid.TryParse(release.ReleaseGroup?.Id, out var parsed))
            {
                continue;
            }

            var releaseGroupMbid = parsed.ToString("D", CultureInfo.InvariantCulture);
            if (!references.TryGetValue(releaseGroupMbid, out var gathered))
            {
                gathered = [];
                references[releaseGroupMbid] = gathered;
                languages[releaseGroupMbid] = [];
                labelDataComplete[releaseGroupMbid] = true;
                languageDataComplete[releaseGroupMbid] = true;
                order.Add(releaseGroupMbid);
            }

            if (release.LabelInfo is null)
            {
                labelDataComplete[releaseGroupMbid] = false;
            }
            else
            {
                foreach (var reference in release.LabelReferences())
                {
                    if (!gathered.Contains(reference))
                    {
                        gathered.Add(reference);
                    }
                }
            }

            if (release.TextRepresentation is null)
            {
                languageDataComplete[releaseGroupMbid] = false;
            }
            else
            {
                var language = release.TextRepresentation.Language?.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(language) &&
                    !languages[releaseGroupMbid].Contains(language, StringComparer.OrdinalIgnoreCase))
                {
                    languages[releaseGroupMbid].Add(language);
                }
            }
        }

        nextOffset += page.Releases.Count;
        var sourceExhausted = page.Releases.Count == 0 ||
            page.Releases.Count < ScanBatchSize ||
            nextOffset >= page.Count;
        if (order.Count == 0)
        {
            TraceStageSkipped("composed", "seed request returned no release groups");
            AppendCandidates(
                scan,
                filter?.Residuals ?? [],
                [],
                nextOffset,
                sourceExhausted,
                truncated: false,
                "composed");
            TraceScanProgress("seed", examinedBefore, page.Releases.Count, order.Count, 0, scan);
            return ScanResult(scan);
        }

        var composed = BuildComposed(fetch, plan.Fingerprint, order);
        if (composed.TooLarge || composed.Request is null)
        {
            TraceStageSkipped(
                "composed",
                composed.TooLarge ? "request target exceeds the maximum size" : "request builder produced no request");
            return new FetchResult<VerifiedScan> { Outcome = FetchOutcome.InvalidRequest };
        }

        var groupFetched = await SearchPageAsync(composed.Request, plan.Settings, "composed", cancellationToken)
            .ConfigureAwait(false);
        if (groupFetched is not { Outcome: FetchOutcome.Success, Value: { } groupPage })
        {
            return new FetchResult<VerifiedScan>
            {
                Outcome = groupFetched.Outcome,
                StatusCode = groupFetched.StatusCode
            };
        }

        var ordinals = order
            .Select((value, index) => (value, index))
            .ToDictionary(pair => pair.value, pair => pair.index, StringComparer.OrdinalIgnoreCase);
        var hits = (from releaseGroup in groupPage.ReleaseGroups
                    let hit = releaseGroup.ToHit()
                    where hit is not null
                    let enriched = hit with
                    {
                        LabelReferences = references.TryGetValue(hit.PrimaryMbid, out var gathered) &&
                            labelDataComplete[hit.PrimaryMbid]
                                ? gathered
                                : null,
                        Languages = languages.TryGetValue(hit.PrimaryMbid, out var gatheredLanguages) &&
                            languageDataComplete[hit.PrimaryMbid]
                                ? gatheredLanguages
                                : null
                    }
                    orderby ordinals.TryGetValue(hit.PrimaryMbid, out var ordinal) ? ordinal : int.MaxValue
                    select enriched).ToArray();
        var filtered = FilterCandidates(filter?.Filters ?? [], hits, "composed");
        AppendCandidates(
            scan,
            filter?.Residuals ?? [],
            filtered,
            nextOffset,
            sourceExhausted,
            truncated: false,
            "composed");
        TraceScanProgress("seed", examinedBefore, page.Releases.Count, order.Count, filtered.Count, scan);
        return ScanResult(scan);
    }

    private void AppendCandidates(
        VerifiedScan scan,
        IReadOnlyList<ResidualCriterion> residuals,
        IReadOnlyList<SearchHit> candidates,
        int nextOffset,
        bool sourceExhausted,
        bool truncated,
        string role)
    {
        var survivors = new List<SearchHit>();
        var warnings = new HashSet<string>();
        foreach (var candidate in candidates)
        {
            var evaluated = EvaluateHit(residuals, candidate);
            foreach (var warning in evaluated.Warnings)
            {
                warnings.Add(warning);
            }

            if (evaluated.Accepted)
            {
                survivors.Add(candidate);
            }
        }

        scan.AddWarnings(warnings);
        var deferred = residuals.Any(residual => residual.Need != ResidualNeed.Hit);
        TraceResidualDelta(role, candidates.Count, survivors.Count, deferred);
        if (deferred)
        {
            if (survivors.Count == 0)
            {
                TraceStageSkipped(
                    "hydration",
                    candidates.Count == 0
                        ? "no candidates survived provider mapping and local filters"
                        : "hit residuals removed all candidates");
            }

            scan.Enqueue(survivors, nextOffset, sourceExhausted, truncated);
        }
        else
        {
            scan.AppendAccepted(survivors, nextOffset, sourceExhausted, truncated);
        }
    }

    private async Task<QueryResult> ExecuteEntityAsync(
        QueryPlan plan,
        FetchPlanNode fetch,
        FilterPlanNode? filter,
        CancellationToken cancellationToken)
    {
        var entityKind = fetch.EntityKind ?? throw new InvalidOperationException("entity kind");
        var key = new EntityCacheKey
        {
            Kind = entityKind,
            Mbid = fetch.DirectValue!,
            Profile = LookupProfile.Search,
            Capabilities = fetch.LookupCapabilities
        };
        var target = key.RequestTarget;
        var fetched = await cache.GetEntityAsync(
            key,
            async token =>
            {
                TraceBuilt(target, plan.Fingerprint, "seed");
                var stopwatch = Stopwatch.StartNew();
                var result = await client.GetEntityAsync(key, plan.Settings, token).ConfigureAwait(false);
                TraceExecuted(target, result.StatusCode, result.Outcome, stopwatch.ElapsedMilliseconds, "seed");
                TraceRemoteResults(target, result.Value is null ? 0 : 1, offset: 0, role: "seed");
                return result;
            },
            cancellationToken).ConfigureAwait(false);
        if (fetched.Outcome == FetchOutcome.NotFound)
        {
            return DirectResult(plan, []);
        }

        if (fetched is not { Outcome: FetchOutcome.Success, Value: { } entity })
        {
            return Failure(plan, fetched.Outcome);
        }

        var hit = entityKind switch
        {
            EntityKind.Artist => entity.Artist.ToHit(),
            EntityKind.ReleaseGroup => entity.ReleaseGroup.ToHit(),
            EntityKind.Release => entity.Release.ToHit(),
            EntityKind.Recording => entity.Recording.ToHit(),
            _ => null
        };
        IReadOnlyList<SearchHit> hits = hit is null ? [] : [hit];
        if (filter is not null)
        {
            return RawCandidates(plan, ApplyResolvedArtistTypes(filter.Filters, hits));
        }

        return DirectResult(plan, FilterCandidates([], hits, "seed"));
    }

    private async Task<QueryResult> ExecuteIsrcAsync(
        QueryPlan plan,
        FetchPlanNode fetch,
        FilterPlanNode? filter,
        CancellationToken cancellationToken)
    {
        var directValue = fetch.DirectValue!;
        var target = string.Concat(
            "isrc/",
            Uri.EscapeDataString(directValue),
            "?fmt=json&inc=artist-credits%2Bisrcs%2Bgenres");
        var fetched = await cache.GetIsrcAsync(
            directValue,
            async token =>
            {
                TraceBuilt(target, plan.Fingerprint, "seed");
                var stopwatch = Stopwatch.StartNew();
                var result = await client.GetIsrcAsync(directValue, plan.Settings, token).ConfigureAwait(false);
                TraceExecuted(target, result.StatusCode, result.Outcome, stopwatch.ElapsedMilliseconds, "seed");
                TraceRemoteResults(
                    target,
                    result.Value is null ? 0 : RawHitCount(fetch.Target, result.Value),
                    0,
                    "seed");
                return result;
            },
            cancellationToken).ConfigureAwait(false);
        if (fetched.Outcome == FetchOutcome.NotFound)
        {
            return DirectResult(plan, []);
        }

        if (fetched is not { Outcome: FetchOutcome.Success, Value: { } page })
        {
            return Failure(plan, fetched.Outcome);
        }

        var candidates = MapPage(QueryTarget.Track, page).ToArray();
        if (filter is not null)
        {
            return RawCandidates(plan, ApplyResolvedArtistTypes(filter.Filters, candidates));
        }

        var filtered = FilterCandidates([], candidates, "seed");
        var offset = PageOffset(plan.RequestedPage, plan.MaxResults);
        var hits = filtered.Skip(offset).Take(plan.MaxResults).ToArray();
        return new QueryResult
        {
            Supported = true,
            Hits = hits,
            PageInfo = BoundedPageInfo(plan.RequestedPage, filtered.Count, plan.MaxResults),
            Warnings = plan.Warnings
        };
    }

    private async Task<ResolvedArtistComposition> ResolveArtistCompositionAsync(
        QueryPlan plan,
        ArtistComposition composition,
        CancellationToken cancellationToken)
    {
        if (composition.Resolution == ArtistCompositionResolution.KnownMbid)
        {
            TraceStageSkipped("artist-composition-resolution", "plan carries the related MBID");
            return new ResolvedArtistComposition
            {
                Outcome = FetchOutcome.Success,
                Artists = composition.RelatedMbid is null
                    ? []
                    : [new ResolvedArtist { Mbid = composition.RelatedMbid }]
            };
        }

        if (composition.Resolution == ArtistCompositionResolution.TypedMbidLookup)
        {
            return await ResolveTypedArtistMbidAsync(plan, composition, cancellationToken).ConfigureAwait(false);
        }

        var cacheKey = new ArtistResolutionCacheKey
        {
            Entity = composition.RelatedEntity,
            Name = NormalizeExactName(composition.RelatedName!),
            NameOperator = composition.RelatedNameOperator,
            RelatedMbid = composition.RelatedMbid
        };
        var fetched = await FetchArtistResolutionAsync(plan, composition, cacheKey, cancellationToken)
            .ConfigureAwait(false);
        if (fetched is not { Outcome: FetchOutcome.Success, Value: { } response })
        {
            return new ResolvedArtistComposition { Outcome = fetched.Outcome };
        }

        var exact = response.Candidates
            .Where(candidate =>
                MatchesResolvedName(candidate, cacheKey.Name, cacheKey.NameOperator) &&
                (composition.RelatedArtistType is null ||
                    string.Equals(
                        candidate.ArtistType,
                        composition.RelatedArtistType,
                        StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        return new ResolvedArtistComposition
        {
            Outcome = FetchOutcome.Success,
            Artists = [.. exact
                .Where(candidate =>
                    composition.RelatedMbid is null ||
                    string.Equals(candidate.Mbid, composition.RelatedMbid, StringComparison.OrdinalIgnoreCase))
                .GroupBy(candidate => candidate.Mbid)
                .Select(candidates => new ResolvedArtist
                {
                    Mbid = candidates.Key,
                    ArtistType = candidates
                        .Select(candidate => candidate.ArtistType)
                        .FirstOrDefault(type => !string.IsNullOrWhiteSpace(type))
                })]
        };
    }

    private async Task<ResolvedArtistComposition> ResolveTypedArtistMbidAsync(
        QueryPlan plan,
        ArtistComposition composition,
        CancellationToken cancellationToken)
    {
        var key = new EntityCacheKey
        {
            Kind = EntityKind.Artist,
            Mbid = composition.RelatedMbid!,
            Profile = LookupProfile.Search,
            Capabilities = composition.RelatedNameOperator == CriteriaValueOperator.ContainsString
                ? LookupCapability.Aliases
                : LookupCapability.None
        };
        var target = key.RequestTarget;
        var fetched = await cache.GetEntityAsync(
            key,
            async token =>
            {
                TraceBuilt(target, plan.Fingerprint, "resolve");
                var stopwatch = Stopwatch.StartNew();
                var result = await client.GetEntityAsync(key, plan.Settings, token).ConfigureAwait(false);
                TraceExecuted(target, result.StatusCode, result.Outcome, stopwatch.ElapsedMilliseconds, "resolve");
                TraceRemoteResults(target, result.Value is null ? 0 : 1, offset: 0, role: "resolve");
                return result;
            },
            cancellationToken).ConfigureAwait(false);
        if (fetched.Outcome == FetchOutcome.NotFound)
        {
            return new ResolvedArtistComposition { Outcome = FetchOutcome.Success };
        }

        if (fetched is not { Outcome: FetchOutcome.Success, Value.Artist: { } artist })
        {
            return new ResolvedArtistComposition { Outcome = fetched.Outcome };
        }

        var candidate = new ArtistResolutionCandidate
        {
            Mbid = composition.RelatedMbid!,
            Name = artist.Name ?? string.Empty,
            Aliases = [.. (artist.Aliases ?? [])
                .Select(alias => alias.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()],
            ArtistType = artist.Type
        };
        var matchesName = composition.RelatedName is null ||
            MatchesResolvedName(
                candidate,
                NormalizeExactName(composition.RelatedName),
                composition.RelatedNameOperator);
        var matchesType = composition.RelatedArtistType is null ||
            string.Equals(artist.Type, composition.RelatedArtistType, StringComparison.OrdinalIgnoreCase);
        return new ResolvedArtistComposition
        {
            Outcome = FetchOutcome.Success,
            Artists = matchesName && matchesType
                ? [new ResolvedArtist { Mbid = composition.RelatedMbid!, ArtistType = artist.Type }]
                : []
        };
    }

    private async Task<FetchResult<ArtistResolutionResponse>> FetchArtistResolutionAsync(
        QueryPlan plan,
        ArtistComposition composition,
        ArtistResolutionCacheKey cacheKey,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ArtistResolutionCandidate>();
        var count = int.MaxValue;
        var offset = 0;
        var returnedCount = 0;
        int? statusCode = null;
        while (offset < count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = BuildArtistResolution(composition, offset);
            if (request is null)
            {
                return new FetchResult<ArtistResolutionResponse> { Outcome = FetchOutcome.InvalidRequest };
            }

            var fetched = await cache.GetArtistResolutionAsync(
                cacheKey with { Offset = offset },
                async token =>
                {
                    TraceBuilt(request.RelativeTarget, request.Fingerprint, "resolve");
                    var stopwatch = Stopwatch.StartNew();
                    var result = await client
                        .ResolveArtistCompositionAsync(request, plan.Settings, token)
                        .ConfigureAwait(false);
                    TraceExecuted(
                        request.RelativeTarget,
                        result.StatusCode,
                        result.Outcome,
                        stopwatch.ElapsedMilliseconds,
                        "resolve");
                    TraceRemoteResults(request.RelativeTarget, result.Value?.ReturnedCount ?? 0, offset, "resolve");
                    return result;
                },
                cancellationToken).ConfigureAwait(false);
            if (fetched is not { Outcome: FetchOutcome.Success, Value: { } response })
            {
                return fetched;
            }

            statusCode = fetched.StatusCode;
            count = Math.Max(0, response.Count);
            returnedCount += response.ReturnedCount;
            candidates.AddRange(response.Candidates);
            if (response.ReturnedCount == 0)
            {
                break;
            }

            offset += response.ReturnedCount;
        }

        return new FetchResult<ArtistResolutionResponse>
        {
            Outcome = FetchOutcome.Success,
            StatusCode = statusCode,
            Value = new ArtistResolutionResponse
            {
                Count = count == int.MaxValue ? 0 : count,
                ReturnedCount = returnedCount,
                Candidates = candidates
            }
        };
    }

    private async Task<ExpandedArtistComposition> ExpandArtistCompositionAsync(
        QueryPlan plan,
        ArtistComposition composition,
        string relatedMbid,
        CancellationToken cancellationToken)
    {
        if (composition.Expansion == ArtistCompositionExpansion.BandMembers)
        {
            return await ExpandBandMembersAsync(plan, relatedMbid, cancellationToken).ConfigureAwait(false);
        }

        var artists = new List<ArtistDto>();
        for (var offset = 0; ;)
        {
            var key = new ArtistBrowseCacheKey
            {
                RelatedEntity = composition.RelatedEntity,
                RelatedMbid = relatedMbid,
                Capabilities = composition.ExpansionCapabilities,
                Limit = BrowsePageSize,
                Offset = offset
            };
            var target = key.RequestTarget!;
            var fetched = await cache.GetArtistBrowsePageAsync(
                key,
                async token =>
                {
                    TraceBuilt(target, plan.Fingerprint, "browse");
                    var stopwatch = Stopwatch.StartNew();
                    var result = await client.BrowseArtistsAsync(key, plan.Settings, token).ConfigureAwait(false);
                    TraceExecuted(target, result.StatusCode, result.Outcome, stopwatch.ElapsedMilliseconds, "browse");
                    TraceRemoteResults(target, result.Value?.Artists?.Count ?? 0, key.Offset, "browse");
                    return result;
                },
                cancellationToken).ConfigureAwait(false);
            if (fetched is not { Outcome: FetchOutcome.Success, Value: { } response })
            {
                return new ExpandedArtistComposition { Outcome = fetched.Outcome };
            }

            var page = response.Artists ?? [];
            artists.AddRange(page);
            if (page.Count == 0 || offset + page.Count >= response.ArtistCount)
            {
                return new ExpandedArtistComposition
                {
                    Outcome = FetchOutcome.Success,
                    Artists = NormalizeArtists(artists)
                };
            }

            offset += page.Count;
        }
    }

    private async Task<ExpandedArtistComposition> ExpandBandMembersAsync(
        QueryPlan plan,
        string bandMbid,
        CancellationToken cancellationToken)
    {
        var key = new EntityCacheKey
        {
            Kind = EntityKind.Artist,
            Mbid = bandMbid,
            Profile = LookupProfile.Search,
            Capabilities = LookupCapability.ArtistRelations
        };
        var target = key.RequestTarget;
        var fetched = await cache.GetEntityAsync(
            key,
            async token =>
            {
                TraceBuilt(target, plan.Fingerprint, "hydration");
                var stopwatch = Stopwatch.StartNew();
                var result = await client.GetEntityAsync(key, plan.Settings, token).ConfigureAwait(false);
                TraceExecuted(target, result.StatusCode, result.Outcome, stopwatch.ElapsedMilliseconds, "hydration");
                TraceRemoteResults(target, result.Value?.Artist?.Relations?.Count ?? 0, offset: 0, role: "hydration");
                return result;
            },
            cancellationToken).ConfigureAwait(false);
        if (fetched is not { Outcome: FetchOutcome.Success, Value.Artist: { } band } ||
            !Guid.TryParse(band.Id, out var parsedBandMbid) ||
            !string.Equals(
                parsedBandMbid.ToString("D", CultureInfo.InvariantCulture),
                bandMbid,
                StringComparison.OrdinalIgnoreCase) ||
            band.Relations is null)
        {
            return new ExpandedArtistComposition
            { Outcome = fetched.Outcome == FetchOutcome.Success ? FetchOutcome.Malformed : fetched.Outcome };
        }

        var expectedDirection = string.Equals(band.Type, "Person", StringComparison.OrdinalIgnoreCase)
            ? "forward"
            : "backward";
        var members = NormalizeArtists(
            band.Relations
                .Where(relation =>
                    string.Equals(
                        relation.Type,
                        Constants.BandMembershipRelationshipKind,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        relation.TargetType,
                        Constants.ArtistRelationTarget,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(relation.Direction, expectedDirection, StringComparison.OrdinalIgnoreCase) &&
                    Guid.TryParse(relation.Artist?.Id, out _))
                .Select(relation => relation.Artist!));
        return new ExpandedArtistComposition { Outcome = FetchOutcome.Success, Artists = members };
    }

    private async Task<QueryResult> ExecuteArtistCompositionPageAsync(
        QueryPlan plan,
        FetchPlanNode fetch,
        ArtistCompositionQuery query,
        CancellationToken cancellationToken)
    {
        var offset = PageOffset(plan.RequestedPage, plan.MaxResults);
        var built = BuildArtistComposition(
            fetch,
            plan.Fingerprint,
            query.IncludedArtistMbids,
            query.ExcludedArtistMbids,
            plan.MaxResults,
            offset);
        if (built.TooLarge)
        {
            TraceStageSkipped("composed", "request target exceeds the maximum size");
            return Oversized(plan);
        }

        if (built.Request is null)
        {
            TraceStageSkipped("composed", "request builder produced no request");
            return EmptyWithWarnings(plan, query.Warnings);
        }

        var fetched = await SearchPageAsync(
            built.Request,
            plan.Settings,
            "composed",
            cancellationToken).ConfigureAwait(false);
        if (fetched is not { Outcome: FetchOutcome.Success, Value: { } page })
        {
            return Failure(plan, fetched.Outcome);
        }

        var rawHitCount = RawHitCount(fetch.Target, page);
        var hits = FilterCandidates([], MapPage(fetch.Target, page), "composed");
        return new QueryResult
        {
            Supported = true,
            Hits = hits,
            PageInfo = DirectPageInfo(plan.RequestedPage, page, rawHitCount, hits.Count, plan.MaxResults),
            Warnings = [.. plan.Warnings.Concat(query.Warnings).Distinct()]
        };
    }

    private async Task<QueryResult> ExecuteBoundedCandidatesAsync(
        QueryPlan plan,
        IReadOnlyList<ResidualCriterion> residuals,
        IReadOnlyList<SearchHit> candidates,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        var remainingSkip = Math.Max(0L, ((long)plan.RequestedPage - 1) * plan.MaxResults);
        var hits = new List<SearchHit>();
        var evaluationWarnings = new HashSet<string>();
        var examined = 0;
        var survivors = 0;
        if (candidates.Count == 0 && residuals.Count > 0)
        {
            TraceStageSkipped("hydration", "local filters removed all expanded candidates");
        }

        foreach (var candidate in candidates)
        {
            examined++;
            var evaluated = residuals.Count == 0
                ? new ResidualEvaluationResult { Accepted = true }
                : await EvaluateCandidateAsync(
                    residuals,
                    plan.Settings,
                    candidate,
                    cancellationToken).ConfigureAwait(false);
            evaluationWarnings.UnionWith(evaluated.Warnings);
            if (!evaluated.Accepted)
            {
                continue;
            }

            survivors++;
            if (remainingSkip > 0)
            {
                remainingSkip--;
                continue;
            }

            hits.Add(evaluated.Candidate ?? candidate);
            if (hits.Count > plan.MaxResults)
            {
                break;
            }
        }

        var hasMore = hits.Count > plan.MaxResults;
        if (hasMore)
        {
            hits.RemoveAt(plan.MaxResults);
        }

        TraceCandidateEvaluation("browse", candidates.Count, examined, survivors, hits.Count);
        return new QueryResult
        {
            Supported = true,
            Hits = hits,
            PageInfo = new ProviderSearchPageInfo { RequestedPage = plan.RequestedPage, HasMore = hasMore },
            Warnings = [.. plan.Warnings
                .Concat(warnings)
                .Concat(evaluationWarnings)
                .Distinct()]
        };
    }

    private Task<FetchResult<SearchPage>> SearchPageAsync(
        Request request,
        Settings settings,
        string role,
        CancellationToken cancellationToken)
    {
        var key = new SearchCacheKey
        {
            Target = request.Target,
            Fingerprint = request.Fingerprint,
            Limit = request.Limit,
            Offset = request.Offset
        };
        return cache.GetSearchPageAsync(
            key,
            async token =>
            {
                TraceBuilt(request.RelativeTarget, request.Fingerprint, role);
                var stopwatch = Stopwatch.StartNew();
                var result = await client.SearchAsync(request, settings, token).ConfigureAwait(false);
                TraceExecuted(
                    request.RelativeTarget,
                    result.StatusCode,
                    result.Outcome,
                    stopwatch.ElapsedMilliseconds,
                    role);
                TraceRemoteResults(
                    request.RelativeTarget,
                    result.Value is null ? 0 : RawHitCount(request.Target, result.Value),
                    request.Offset,
                    role);
                return result;
            },
            cancellationToken);
    }

    private async Task<ResidualEvaluationResult> EvaluateCandidateAsync(
        IReadOnlyList<ResidualCriterion> residuals,
        Settings settings,
        SearchHit hit,
        CancellationToken cancellationToken)
    {
        var hitResult = EvaluateHit(residuals, hit);
        if (!hitResult.Accepted)
        {
            return hitResult;
        }

        var deferred = await EvaluateDeferredAsync(residuals, settings, hit, cancellationToken).ConfigureAwait(false);
        return new ResidualEvaluationResult
        {
            Accepted = deferred.Accepted,
            Warnings = [.. hitResult.Warnings.Concat(deferred.Warnings).Distinct()],
            Candidate = deferred.Candidate ?? hit
        };
    }

    private IReadOnlyList<SearchHit> FilterCandidates(
        IReadOnlyList<QueryFilter> filters,
        IEnumerable<SearchHit> candidates,
        string role)
    {
        var input = ApplyResolvedArtistTypes(filters, candidates);
        var output = Filter(filters, input);
        foreach (var filter in filters.Where(filter => filter.VerifyLocally))
        {
            var rejected = input.Count(candidate => !Matches(filter, candidate));
            _currentTrace.Value?.RecordStage(
                _currentStageNodeId.Value ?? _currentNodeId.Value,
                role + "/predicate/" + filter.Kind,
                "evaluated",
                null,
                input.Length,
                input.Length - rejected);
        }

        _currentTrace.Value?.RecordStage(
            _currentStageNodeId.Value ?? _currentNodeId.Value,
            role + "/local-filter",
            "executed",
            null,
            input.Length,
            output.Count);
        return output;
    }

    private void TraceResidualDelta(string role, int inputCount, int outputCount, bool deferred)
    {
        _currentTrace.Value?.RecordStage(
            _currentStageNodeId.Value ?? _currentNodeId.Value,
            role + "/hit-residuals",
            deferred ? "deferred" : "executed",
            null,
            inputCount,
            outputCount);
    }

    private void TraceScanProgress(
        string role,
        int examinedBefore,
        int batchCount,
        int mappedCount,
        int localCount,
        VerifiedScan scan)
    {
        if (_currentTrace.Value is null)
        {
            return;
        }

        var snapshot = scan.Read();
        _currentTrace.Value.RecordStage(
            _currentStageNodeId.Value ?? _currentNodeId.Value,
            role + "/scan",
            snapshot.SourceExhausted ? "source-exhausted" : "continued",
            snapshot.Truncated ? "upstream-truncated" : null,
            examinedBefore + batchCount,
            snapshot.HitCount + snapshot.PendingCount,
            snapshot.NextOffset / Math.Max(1, ScanBatchSize));
    }

    private void TraceScanSnapshot(string role, VerifiedScan scan)
    {
        if (_currentTrace.Value is null)
        {
            return;
        }

        var snapshot = scan.Read();
        _currentTrace.Value.RecordStage(
            _currentStageNodeId.Value ?? _currentNodeId.Value,
            role + "/scan-snapshot",
            snapshot.Exhausted ? "exhausted" : "open",
            snapshot.Truncated ? "upstream-truncated" : null,
            snapshot.NextOffset,
            snapshot.HitCount,
            snapshot.NextOffset / Math.Max(1, ScanBatchSize));
    }

    private void TraceDeferredDelta(string candidate, bool accepted, VerifiedScan scan)
    {
        if (_currentTrace.Value is null)
        {
            return;
        }

        var snapshot = scan.Read();
        _currentTrace.Value.RecordStage(
            _currentStageNodeId.Value ?? _currentNodeId.Value,
            "hydration/deferred-residual",
            accepted ? "accepted" : "rejected",
            null,
            1,
            accepted ? 1 : 0,
            snapshot.NextOffset / Math.Max(1, ScanBatchSize));
    }

    private void TraceCandidateEvaluation(
        string role,
        int availableCount,
        int examinedCount,
        int survivorCount,
        int pageCount)
    {
        _currentTrace.Value?.RecordStage(
            _currentStageNodeId.Value ?? _currentNodeId.Value,
            role + "/candidate-evaluation",
            examinedCount < availableCount ? "settled-early" : "exhausted",
            null,
            examinedCount,
            survivorCount);
    }

    private void TraceBuilt(string relativeTarget, string diagnosticKey, string role)
    {
        _currentTrace.Value?.RecordRequestBuilt(_currentNodeId.Value, role, relativeTarget);
    }

    private void TraceExecuted(
        string relativeTarget,
        int? statusCode,
        FetchOutcome outcome,
        long elapsedMilliseconds,
        string role)
    {
        _currentTrace.Value?.RecordRequestExecuted(
            _currentNodeId.Value,
            role,
            relativeTarget,
            statusCode,
            outcome,
            elapsedMilliseconds);
    }

    private void TraceStageSkipped(string role, string reason)
    {
        _currentTrace.Value?.RecordStage(_currentStageNodeId.Value ?? _currentNodeId.Value, role, "skipped", reason);
    }

    private void TraceRemoteResults(string relativeTarget, int count, int offset, string role)
    {
        _currentTrace.Value?.RecordRemoteRows(_currentNodeId.Value, role, relativeTarget, count, offset);
    }

    private void TraceResult(QueryPlan plan, QueryResult result, TimeSpan elapsed)
    {
        if (_currentTrace.Value is { } trace)
        {
            logger.LogTrace(
                "EXEC MusicBrainz {ExecutionJson}",
                plan.SerializeExecution(result, trace, elapsed, TerminalReason(plan, result)));
        }
    }

    private async Task<FetchResult<ResidualEntityData>> GetEntityDataAsync(
        SearchHit hit,
        LookupCapability capabilities,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var key = new EntityCacheKey
        {
            Kind = EntityKindFor(hit.Target),
            Mbid = hit.PrimaryMbid,
            Profile = LookupProfile.Search,
            Capabilities = capabilities
        };
        var fetched = await GetEntityAsync(key, settings, cancellationToken).ConfigureAwait(false);
        if (fetched is not { Outcome: FetchOutcome.Success, Value: { } entity })
        {
            return Failed<ResidualEntityData>(fetched);
        }

        var entityId = hit.Target switch
        {
            QueryTarget.Artist => entity.Artist?.Id,
            QueryTarget.Album => entity.ReleaseGroup?.Id,
            QueryTarget.Track => entity.Recording?.Id,
            _ => null
        };
        if (!MatchesMbid(entityId, hit.PrimaryMbid))
        {
            return Outcome<ResidualEntityData>(FetchOutcome.Malformed, fetched.StatusCode);
        }

        return new FetchResult<ResidualEntityData>
        {
            Outcome = FetchOutcome.Success,
            StatusCode = fetched.StatusCode,
            Value = hit.Target switch
            {
                QueryTarget.Artist => new ResidualEntityData
                {
                    Aliases = entity.Artist?.Aliases,
                    UrlRelations = entity.Artist?.Relations,
                    Genres = entity.Artist.ToHit()?.Genres,
                    ExternalIds = entity.Artist.ToHit()?.ExternalIds
                },
                QueryTarget.Album => new ResidualEntityData
                {
                    Aliases = entity.ReleaseGroup?.Aliases,
                    UrlRelations = entity.ReleaseGroup?.Relations,
                    Genres = entity.ReleaseGroup.ToHit()?.Genres,
                    ExternalIds = entity.ReleaseGroup.ToHit()?.ExternalIds
                },
                QueryTarget.Track => new ResidualEntityData
                {
                    Aliases = entity.Recording?.Aliases,
                    UrlRelations = entity.Recording?.Relations,
                    Genres = entity.Recording.ToHit()?.Genres,
                    ExternalIds = entity.Recording.ToHit()?.ExternalIds
                },
                _ => new ResidualEntityData()
            }
        };
    }

    private async Task<FetchResult<ResidualEntityData>> GetRepresentativeReleaseLanguageAsync(
        SearchHit hit,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var groupKey = new EntityCacheKey
        {
            Kind = EntityKind.ReleaseGroup,
            Mbid = hit.PrimaryMbid,
            Profile = LookupProfile.Search,
            Capabilities = LookupCapability.Releases
        };
        var groupFetched = await GetEntityAsync(groupKey, settings, cancellationToken).ConfigureAwait(false);
        if (groupFetched is not { Outcome: FetchOutcome.Success, Value.ReleaseGroup: { } releaseGroup })
        {
            return Failed<ResidualEntityData>(groupFetched);
        }

        if (!MatchesMbid(releaseGroup.Id, hit.PrimaryMbid) || releaseGroup.Releases is null)
        {
            return Outcome<ResidualEntityData>(FetchOutcome.Malformed, groupFetched.StatusCode);
        }

        var representative = releaseGroup.SelectRepresentativeRelease();
        var language = representative?.TextRepresentation?.Language?.Trim().ToLowerInvariant();
        return Success(new ResidualEntityData { Language = string.IsNullOrWhiteSpace(language) ? null : language });
    }

    private async Task<FetchResult<ResidualEntityData>> GetRepresentativeReleaseDurationAsync(
        SearchHit hit,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var groupKey = new EntityCacheKey
        {
            Kind = EntityKind.ReleaseGroup,
            Mbid = hit.PrimaryMbid,
            Profile = LookupProfile.Search,
            Capabilities = LookupCapability.Releases | LookupCapability.ReleaseMediaSummary
        };
        var groupFetched = await GetEntityAsync(groupKey, settings, cancellationToken).ConfigureAwait(false);
        if (groupFetched is not { Outcome: FetchOutcome.Success, Value.ReleaseGroup: { } releaseGroup })
        {
            return Failed<ResidualEntityData>(groupFetched);
        }

        if (!MatchesMbid(releaseGroup.Id, hit.PrimaryMbid) || releaseGroup.Releases is null)
        {
            return Outcome<ResidualEntityData>(FetchOutcome.Malformed, groupFetched.StatusCode);
        }

        var representative = releaseGroup.SelectDurationRelease();
        if (representative is null || !Guid.TryParse(representative.Id, out var releaseMbid))
        {
            return releaseGroup.Releases.Count == 0
                ? Success(new ResidualEntityData())
                : Outcome<ResidualEntityData>(FetchOutcome.Malformed);
        }

        if (representative.Media?.Any(medium => medium?.Tracks is not null) == true)
        {
            return ReleaseDuration(representative);
        }

        var releaseKey = new EntityCacheKey
        {
            Kind = EntityKind.Release,
            Mbid = releaseMbid.ToString("D"),
            Profile = LookupProfile.Search,
            Capabilities = LookupCapability.ReleaseMedia
        };
        var releaseFetched = await GetEntityAsync(releaseKey, settings, cancellationToken).ConfigureAwait(false);
        if (releaseFetched is not { Outcome: FetchOutcome.Success, Value.Release: { } release })
        {
            return Failed<ResidualEntityData>(releaseFetched);
        }

        if (!MatchesMbid(release.Id, releaseMbid.ToString("D")) || release.Media is null)
        {
            return Outcome<ResidualEntityData>(FetchOutcome.Malformed, releaseFetched.StatusCode);
        }

        return ReleaseDuration(release);
    }

    private Task<FetchResult<Uri>> GetCoverAsync(SearchHit hit, Settings settings, CancellationToken cancellationToken)
    {
        var key = new CoverCacheKey { ReleaseGroupMbid = hit.PrimaryMbid, ThumbnailSize = CoverThumbnailSize };
        var target = hit.PrimaryMbid.ReleaseGroupFrontTarget(CoverThumbnailSize);
        return cache.GetCoverAsync(
            key,
            async token =>
            {
                TraceBuilt(target, hit.PrimaryMbid, "cover-art");
                var stopwatch = Stopwatch.StartNew();
                var result = await coverClient.GetReleaseGroupFrontAsync(
                    hit.PrimaryMbid,
                    CoverThumbnailSize,
                    settings,
                    token).ConfigureAwait(false);
                TraceExecuted(target, result.StatusCode, result.Outcome, stopwatch.ElapsedMilliseconds, "cover-art");
                TraceRemoteResults(target, result.Value is null ? 0 : 1, offset: 0, "cover-art");
                return result;
            },
            cancellationToken);
    }

    private Task<FetchResult<EntityResponseDto>> GetEntityAsync(
        EntityCacheKey key,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var target = key.RequestTarget;
        return cache.GetEntityAsync(
            key,
            async token =>
            {
                TraceBuilt(target, key.Mbid, "hydration");
                var stopwatch = Stopwatch.StartNew();
                var result = await client.GetEntityAsync(key, settings, token).ConfigureAwait(false);
                TraceExecuted(target, result.StatusCode, result.Outcome, stopwatch.ElapsedMilliseconds, "hydration");
                TraceRemoteResults(target, result.Value is null ? 0 : 1, offset: 0, "hydration");
                return result;
            },
            cancellationToken);
    }

    private ResidualEvaluationResult EvaluateHit(IReadOnlyList<ResidualCriterion> residuals, SearchHit hit)
    {
        var warnings = new HashSet<string>();
        var hitResiduals = Residuals(residuals, ResidualNeed.Hit);
        if (hitResiduals.Count == 0)
        {
            TraceStage("hit", "skipped", hit, hitResiduals.Count, accepted: true);
            return Result(accepted: true, warnings);
        }

        for (var index = 0; index < hitResiduals.Count; index++)
        {
            var residual = hitResiduals[index];
            var outcome = residual.Kind switch { ResidualKind.Genre => MatchGenres(residual, hit.Genres), _ => null };
            TraceResidual(
                "hit",
                residual,
                hit,
                accepted: outcome != false,
                reason: outcome is null
                    ? "unverifiable retained"
                    : outcome.Value
                        ? "evaluated"
                        : "candidate rejected");
            if (outcome == false)
            {
                for (var remaining = index + 1; remaining < hitResiduals.Count; remaining++)
                {
                    TraceResidual(
                        "hit",
                        hitResiduals[remaining],
                        hit,
                        accepted: true,
                        reason: "prior residual rejected",
                        inputCount: 0);
                }

                TraceStage("hit", "completed", hit, hitResiduals.Count, accepted: false);
                TraceDownstreamSkipped(residuals, hit, "hit");
                return Result(accepted: false, warnings);
            }

            if (outcome is null)
            {
                warnings.Add(UnverifiableWarning(residual.Kind, "search-hit data was omitted"));
            }
        }

        TraceStage("hit", "completed", hit, hitResiduals.Count, accepted: true);
        return Result(accepted: true, warnings);
    }

    private async Task<ResidualEvaluationResult> EvaluateDeferredAsync(
        IReadOnlyList<ResidualCriterion> residuals,
        Settings settings,
        SearchHit hit,
        CancellationToken cancellationToken)
    {
        var warnings = new HashSet<string>();
        var evaluatedHit = hit;
        var aliasResiduals = Residuals(residuals, ResidualNeed.EntityAliases);
        var referenceResiduals = Residuals(residuals, ResidualNeed.EntityUrlRelations);
        var genreResiduals = Residuals(residuals, ResidualNeed.EntityGenres);
        var externalIdResiduals = Residuals(residuals, ResidualNeed.EntityExternalIds);
        var labelResiduals = Residuals(residuals, ResidualNeed.RepresentativeReleaseLabels);
        var aliases = hit.Aliases;
        var relations = hit.UrlRelations;
        var genres = hit.Genres;
        IReadOnlyList<ExternalIdentifier>? externalIds = null;
        var labels = hit.LabelReferences;
        var labelPrefetchFailed = false;
        var labelsEvaluated = false;
        var entityResidualCount = aliasResiduals.Count + referenceResiduals.Count + genreResiduals.Count +
            externalIdResiduals.Count;
        var aliasesEvaluated = false;
        var referencesEvaluated = false;
        var genresEvaluated = false;
        var externalIdsEvaluated = false;
        if (entityResidualCount == 0)
        {
            TraceStage("entity", "skipped", hit, residualCount: 0, accepted: true);
        }

        if (aliases is not null)
        {
            aliasesEvaluated = true;
            if (!AllMatch("entity", hit, aliasResiduals, residual => MatchAliases(residual, hit.Title, aliases)))
            {
                TraceUnevaluatedEntityResiduals();
                TraceStage("entity", "completed", hit, entityResidualCount, accepted: false);
                TraceDownstreamSkipped(residuals, hit, "entity");
                return Result(accepted: false, warnings);
            }
        }

        if (relations is not null)
        {
            referencesEvaluated = true;
            if (!AllMatch("entity", hit, referenceResiduals, residual => MatchReferences(residual, relations)))
            {
                TraceUnevaluatedEntityResiduals();
                TraceStage("entity", "completed", hit, entityResidualCount, accepted: false);
                TraceDownstreamSkipped(residuals, hit, "entity");
                return Result(accepted: false, warnings);
            }
        }

        if (genres is not null)
        {
            genresEvaluated = true;
            if (!AllMatch("entity", hit, genreResiduals, residual => MatchGenres(residual, genres) == true))
            {
                TraceUnevaluatedEntityResiduals();
                TraceStage("entity", "completed", hit, entityResidualCount, accepted: false);
                TraceDownstreamSkipped(residuals, hit, "entity");
                return Result(accepted: false, warnings);
            }
        }

        var capabilities = LookupCapability.None;
        if (aliasResiduals.Count > 0 && aliases is null)
        {
            capabilities |= LookupCapability.Aliases;
        }

        if (referenceResiduals.Count > 0 && relations is null)
        {
            capabilities |= LookupCapability.UrlRelations;
        }

        if (genreResiduals.Count > 0 && genres is null)
        {
            capabilities |= LookupCapability.Genres;
        }

        if (externalIdResiduals.Count > 0)
        {
            capabilities |= LookupCapability.ExternalIds;
        }

        if (capabilities != LookupCapability.None && labelResiduals.Count > 0 && labels is null &&
            hit.Target == QueryTarget.Album)
        {
            capabilities |= LookupCapability.Releases;
        }

        if (capabilities != LookupCapability.None)
        {
            TraceHydrationRequested("entity", hit, capabilities.ToString());
            var fetched = await GetEntityDataAsync(
                hit,
                capabilities,
                settings,
                cancellationToken).ConfigureAwait(false);
            TraceHydrationCompleted("entity", hit, capabilities.ToString(), fetched.Outcome);
            if (fetched is { Outcome: FetchOutcome.Success, Value: { } entity })
            {
                if ((capabilities & LookupCapability.Aliases) != 0)
                {
                    aliases = entity.Aliases;
                    if (aliases is null)
                    {
                        warnings.Add(UnverifiableWarning(
                            ResidualKind.AlternateText,
                            "the entity response omitted requested alias data"));
                    }
                }

                if ((capabilities & LookupCapability.UrlRelations) != 0)
                {
                    relations = entity.UrlRelations;
                    if (relations is null)
                    {
                        warnings.Add(UnverifiableWarning(
                            ResidualKind.Reference,
                            "the entity response omitted requested URL-relation data"));
                    }
                }

                if ((capabilities & LookupCapability.Genres) != 0)
                {
                    genres = entity.Genres;
                    if (genres is null)
                    {
                        warnings.Add(UnverifiableWarning(
                            ResidualKind.Genre,
                            "the entity response omitted requested genre data"));
                    }
                }

                if ((capabilities & LookupCapability.ExternalIds) != 0)
                {
                    externalIds = entity.ExternalIds;
                    if (externalIds is null)
                    {
                        warnings.Add(UnverifiableWarning(
                            ResidualKind.ExternalId,
                            "the entity response omitted requested identifier data"));
                    }
                }
            }
            else
            {
                if ((capabilities & LookupCapability.Aliases) != 0)
                {
                    warnings.Add(UnverifiableWarning(ResidualKind.AlternateText, "entity hydration failed"));
                }

                if ((capabilities & LookupCapability.UrlRelations) != 0)
                {
                    warnings.Add(UnverifiableWarning(ResidualKind.Reference, "entity hydration failed"));
                }

                if ((capabilities & LookupCapability.Genres) != 0)
                {
                    warnings.Add(UnverifiableWarning(ResidualKind.Genre, "entity hydration failed"));
                }

                if ((capabilities & LookupCapability.ExternalIds) != 0)
                {
                    warnings.Add(UnverifiableWarning(ResidualKind.ExternalId, "entity hydration failed"));
                }

                if ((capabilities & LookupCapability.Releases) != 0)
                {
                    labelPrefetchFailed = true;
                    warnings.Add(UnverifiableWarning(
                        ResidualKind.LabelRelationship,
                        "representative-release hydration failed"));
                }
            }
        }

        if (!aliasesEvaluated && aliases is not null)
        {
            aliasesEvaluated = true;
            if (!AllMatch("entity", hit, aliasResiduals, residual => MatchAliases(residual, hit.Title, aliases)))
            {
                TraceUnevaluatedEntityResiduals();
                TraceStage("entity", "completed", hit, entityResidualCount, accepted: false);
                TraceDownstreamSkipped(residuals, hit, "entity");
                return Result(accepted: false, warnings);
            }
        }

        if (!referencesEvaluated && relations is not null)
        {
            referencesEvaluated = true;
            if (!AllMatch("entity", hit, referenceResiduals, residual => MatchReferences(residual, relations)))
            {
                TraceUnevaluatedEntityResiduals();
                TraceStage("entity", "completed", hit, entityResidualCount, accepted: false);
                TraceDownstreamSkipped(residuals, hit, "entity");
                return Result(accepted: false, warnings);
            }
        }

        if (!genresEvaluated && genres is not null)
        {
            genresEvaluated = true;
            if (!AllMatch("entity", hit, genreResiduals, residual => MatchGenres(residual, genres) == true))
            {
                TraceUnevaluatedEntityResiduals();
                TraceStage("entity", "completed", hit, entityResidualCount, accepted: false);
                TraceDownstreamSkipped(residuals, hit, "entity");
                return Result(accepted: false, warnings);
            }
        }

        if (!externalIdsEvaluated && externalIds is not null)
        {
            externalIdsEvaluated = true;
            if (!AllMatch("entity", hit, externalIdResiduals, residual => MatchExternalIds(residual, externalIds)))
            {
                TraceUnevaluatedEntityResiduals();
                TraceStage("entity", "completed", hit, entityResidualCount, accepted: false);
                TraceDownstreamSkipped(residuals, hit, "entity");
                return Result(accepted: false, warnings);
            }

            evaluatedHit = evaluatedHit with { ExternalIds = externalIds };
        }

        if (!aliasesEvaluated)
        {
            TraceResidualsRetained("entity", hit, aliasResiduals);
            aliasesEvaluated = true;
        }

        if (!referencesEvaluated)
        {
            TraceResidualsRetained("entity", hit, referenceResiduals);
            referencesEvaluated = true;
        }

        if (!genresEvaluated)
        {
            TraceResidualsRetained("entity", hit, genreResiduals);
            genresEvaluated = true;
        }

        if (!externalIdsEvaluated)
        {
            TraceResidualsRetained("entity", hit, externalIdResiduals);
            externalIdsEvaluated = true;
        }

        if (entityResidualCount > 0)
        {
            TraceStage("entity", "completed", hit, entityResidualCount, accepted: true);
        }

        if (labelResiduals.Count > 0)
        {
            if (labels is not null &&
                !AllMatch("representative-labels", hit, labelResiduals, residual => MatchLabels(residual, labels)))
            {
                labelsEvaluated = true;
                TraceStage("representative-labels", "completed", hit, labelResiduals.Count, accepted: false);
                TraceDownstreamSkipped(residuals, hit, "representative-labels");
                return Result(accepted: false, warnings);
            }

            if (labels is not null)
            {
                labelsEvaluated = true;
            }

            if (labels is null && !labelPrefetchFailed)
            {
                TraceHydrationRequested(
                    "representative-labels",
                    hit,
                    ResidualNeed.RepresentativeReleaseLabels.ToString());
                var fetched = await GetRepresentativeReleaseLabelsAsync(
                    hit,
                    settings,
                    cancellationToken).ConfigureAwait(false);
                TraceHydrationCompleted(
                    "representative-labels",
                    hit,
                    ResidualNeed.RepresentativeReleaseLabels.ToString(),
                    fetched.Outcome);
                if (fetched is { Outcome: FetchOutcome.Success, Value: { } value })
                {
                    labels = value;
                }
                else
                {
                    warnings.Add(UnverifiableWarning(
                        ResidualKind.LabelRelationship,
                        "representative-release hydration failed"));
                }
            }

            if (!labelsEvaluated && labels is not null &&
                !AllMatch("representative-labels", hit, labelResiduals, residual => MatchLabels(residual, labels)))
            {
                labelsEvaluated = true;
                TraceStage("representative-labels", "completed", hit, labelResiduals.Count, accepted: false);
                TraceDownstreamSkipped(residuals, hit, "representative-labels");
                return Result(accepted: false, warnings);
            }

            if (labels is not null)
            {
                labelsEvaluated = true;
                evaluatedHit = evaluatedHit with { LabelReferences = labels };
            }

            if (labels is null)
            {
                TraceResidualsRetained("representative-labels", hit, labelResiduals);
            }

            TraceStage("representative-labels", "completed", hit, labelResiduals.Count, accepted: true);
        }
        else
        {
            TraceStage("representative-labels", "skipped", hit, residualCount: 0, accepted: true);
        }

        var languageResiduals = Residuals(residuals, ResidualNeed.RepresentativeReleaseLanguage);
        if (languageResiduals.Count > 0)
        {
            TraceHydrationRequested(
                "representative-language",
                hit,
                ResidualNeed.RepresentativeReleaseLanguage.ToString());
            var fetched = await GetRepresentativeReleaseLanguageAsync(
                hit,
                settings,
                cancellationToken).ConfigureAwait(false);
            TraceHydrationCompleted(
                "representative-language",
                hit,
                ResidualNeed.RepresentativeReleaseLanguage.ToString(),
                fetched.Outcome);
            if (fetched is not { Outcome: FetchOutcome.Success, Value: { } value })
            {
                warnings.Add(UnverifiableWarning(ResidualKind.Language, "representative-release hydration failed"));
                TraceResidualsRetained("representative-language", hit, languageResiduals);
            }
            else
            {
                evaluatedHit = evaluatedHit with { RepresentativeLanguage = value.Language };
                if (!AllMatch(
                    "representative-language",
                    hit,
                    languageResiduals,
                    residual => MatchLanguage(residual, value.Language)))
                {
                    TraceStage("representative-language", "completed", hit, languageResiduals.Count, accepted: false);
                    TraceDownstreamSkipped(residuals, hit, "representative-language");
                    return Result(accepted: false, warnings);
                }
            }

            TraceStage("representative-language", "completed", hit, languageResiduals.Count, accepted: true);
        }
        else
        {
            TraceStage("representative-language", "skipped", hit, residualCount: 0, accepted: true);
        }

        var durationResiduals = Residuals(residuals, ResidualNeed.RepresentativeReleaseDuration);
        if (durationResiduals.Count > 0)
        {
            TraceHydrationRequested(
                "representative-duration",
                hit,
                ResidualNeed.RepresentativeReleaseDuration.ToString());
            var fetched = await GetRepresentativeReleaseDurationAsync(
                hit,
                settings,
                cancellationToken).ConfigureAwait(false);
            TraceHydrationCompleted(
                "representative-duration",
                hit,
                ResidualNeed.RepresentativeReleaseDuration.ToString(),
                fetched.Outcome);
            if (fetched is not { Outcome: FetchOutcome.Success, Value: { } value })
            {
                warnings.Add(UnverifiableWarning(ResidualKind.Duration, "representative-release hydration failed"));
                TraceResidualsRetained("representative-duration", hit, durationResiduals);
            }
            else
            {
                evaluatedHit = evaluatedHit with { Duration = value.Duration };
                if (!AllMatch(
                    "representative-duration",
                    hit,
                    durationResiduals,
                    residual => MatchDuration(residual, value.Duration)))
                {
                    TraceStage("representative-duration", "completed", hit, durationResiduals.Count, accepted: false);
                    TraceDownstreamSkipped(residuals, hit, "representative-duration");
                    return Result(accepted: false, warnings);
                }
            }

            TraceStage("representative-duration", "completed", hit, durationResiduals.Count, accepted: true);
        }
        else
        {
            TraceStage("representative-duration", "skipped", hit, residualCount: 0, accepted: true);
        }

        var coverResiduals = Residuals(residuals, ResidualNeed.CoverArt);
        if (coverResiduals.Count > 0)
        {
            TraceHydrationRequested("cover", hit, ResidualNeed.CoverArt.ToString());
            var fetched = await GetCoverAsync(
                hit,
                settings,
                cancellationToken).ConfigureAwait(false);
            TraceHydrationCompleted("cover", hit, ResidualNeed.CoverArt.ToString(), fetched.Outcome);
            var coverUrl = fetched.Outcome switch
            {
                FetchOutcome.Success => fetched.Value,
                FetchOutcome.NotFound => null,
                _ => null
            };
            if (fetched.Outcome is not (FetchOutcome.Success or FetchOutcome.NotFound))
            {
                warnings.Add(UnverifiableWarning(ResidualKind.ImagePresence, "cover-art lookup failed"));
                TraceResidualsRetained("cover", hit, coverResiduals);
            }
            else if (!AllMatch("cover", hit, coverResiduals, residual => MatchImage(residual, coverUrl)))
            {
                TraceStage("cover", "completed", hit, coverResiduals.Count, accepted: false);
                return Result(accepted: false, warnings);
            }

            TraceStage("cover", "completed", hit, coverResiduals.Count, accepted: true);
        }
        else
        {
            TraceStage("cover", "skipped", hit, residualCount: 0, accepted: true);
        }

        return Result(accepted: true, warnings, evaluatedHit);

        void TraceUnevaluatedEntityResiduals()
        {
            if (!aliasesEvaluated)
            {
                TraceResidualsSkipped("entity", hit, aliasResiduals, "prior residual rejected");
            }

            if (!referencesEvaluated)
            {
                TraceResidualsSkipped("entity", hit, referenceResiduals, "prior residual rejected");
            }

            if (!genresEvaluated)
            {
                TraceResidualsSkipped("entity", hit, genreResiduals, "prior residual rejected");
            }

            if (!externalIdsEvaluated)
            {
                TraceResidualsSkipped("entity", hit, externalIdResiduals, "prior residual rejected");
            }
        }
    }

    private IReadOnlyList<ResidualCriterion> Residuals(IReadOnlyList<ResidualCriterion> residuals, ResidualNeed need)
    {
        return [.. residuals.Where(value => value.Need == need)];
    }

    private bool? MatchGenres(ResidualCriterion residual, IReadOnlyList<string>? genres)
    {
        return genres is null
            ? null
            : ApplyCollectionNegation(
                residual,
                residual.Alternatives.Any(alternative => genres.Any(genre =>
                    alternative.Predicates.All(predicate => MatchString(predicate, genre)))));
    }

    private bool MatchAliases(
        ResidualCriterion residual,
        string? title,
        IReadOnlyList<AliasDto> aliases)
    {
        return ApplyCollectionNegation(
            residual,
            residual.IncludePrimaryTitle &&
            residual.Alternatives.Any(alternative =>
                alternative.Predicates.All(predicate =>
                    predicate.Field == ResidualField.AlternateTextText && MatchString(predicate, title))) ||
            residual.Alternatives.Any(alternative => aliases.Any(alias =>
                alternative.Predicates.All(predicate => predicate.Field switch
                {
                    ResidualField.AlternateTextKind => MatchString(predicate, Constants.AlternateTextKind),
                    ResidualField.AlternateTextLanguage => MatchString(predicate, alias.Locale),
                    ResidualField.AlternateTextText => MatchString(predicate, alias.Name),
                    _ => false
                }))));
    }

    private bool MatchReferences(
        ResidualCriterion residual,
        IReadOnlyList<RelationDto> relations)
    {
        return ApplyCollectionNegation(
            residual,
            residual.Alternatives.Any(alternative => relations
                .Project()
                .Any(reference => alternative.Predicates.All(predicate => predicate.Field switch
                {
                    ResidualField.ReferenceKind => MatchString(predicate, reference.Kind),
                    ResidualField.ReferenceUrl => MatchString(predicate, reference.Url.AbsoluteUri),
                    _ => false
                }))));
    }

    private bool MatchLabels(
        ResidualCriterion residual,
        IReadOnlyList<LabelReference> labels)
    {
        return ApplyCollectionNegation(
            residual,
            residual.Alternatives.Any(alternative => labels.Any(label =>
                alternative.Predicates.All(predicate => predicate.Field switch
                {
                    ResidualField.LabelKind => MatchString(predicate, Constants.LabelSubjectKind),
                    ResidualField.LabelSubject => MatchString(predicate, label.Name),
                    ResidualField.LabelExternalId => MatchString(predicate, label.Mbid),
                    _ => false
                }))));
    }

    private bool MatchExternalIds(
        ResidualCriterion residual,
        IReadOnlyList<ExternalIdentifier> externalIds)
    {
        return ApplyCollectionNegation(
            residual,
            residual.Alternatives.Any(alternative => externalIds.Any(externalId =>
                alternative.Predicates.All(predicate => predicate.Field switch
                {
                    ResidualField.ExternalIdSource => MatchString(predicate, externalId.Source),
                    ResidualField.ExternalIdValue => MatchString(predicate, externalId.Value),
                    _ => false
                }))));
    }

    private bool MatchLanguage(ResidualCriterion residual, string? language)
    {
        return residual.Alternatives.All(alternative => alternative.Predicates.All(predicate =>
            predicate.Field == ResidualField.Language && MatchString(predicate, language)));
    }

    private bool MatchDuration(ResidualCriterion residual, TimeSpan? duration)
    {
        return residual.Alternatives.All(alternative => alternative.Predicates.All(predicate =>
            predicate.Field == ResidualField.Duration &&
            predicate.Operator.MatchesDuration(predicate.DurationMilliseconds, duration) != predicate.Negate));
    }

    private bool MatchImage(ResidualCriterion residual, Uri? coverUrl)
    {
        var present = coverUrl is not null;
        var matches = present &&
            residual.Alternatives.Any(alternative => alternative.Predicates.All(predicate =>
                predicate.Field switch
                {
                    ResidualField.ImagePresence => MatchPresence(predicate, present),
                    ResidualField.ImageUrl => MatchString(predicate, coverUrl?.AbsoluteUri),
                    _ => false
                }));
        return ApplyCollectionNegation(residual, matches);
    }

    private bool MatchPresence(ResidualPredicate predicate, bool present)
    {
        var matches = predicate.Operator switch
        {
            CriteriaValueOperator.IsNull => !present,
            CriteriaValueOperator.IsNotNull => present,
            _ => present
        };
        return predicate.Negate ? !matches : matches;
    }

    private bool MatchString(ResidualPredicate predicate, string? candidate)
    {
        var matches = predicate.Operator.MatchesString(predicate.Values, candidate);
        return predicate.Negate ? !matches : matches;
    }

    private bool ApplyCollectionNegation(ResidualCriterion residual, bool matches)
    {
        return residual.Negate ? !matches : matches;
    }

    private ResidualEvaluationResult Result(
        bool accepted,
        IEnumerable<string> warnings,
        SearchHit? candidate = null)
    {
        return new()
        {
            Accepted = accepted,
            Warnings = [.. warnings],
            Candidate = candidate
        };
    }

    private string UnverifiableWarning(ResidualKind kind, string reason)
    {
        var (diagnosticFilter, filter) = kind switch
        {
            ResidualKind.Genre => ("genre", "Genre"),
            ResidualKind.AlternateText => ("alternate-text", "Alternate text"),
            ResidualKind.Reference => ("reference", "Reference"),
            ResidualKind.LabelRelationship => ("label", "Label"),
            ResidualKind.Language => ("language", "Language"),
            ResidualKind.Duration => ("duration", "Duration"),
            ResidualKind.ImagePresence => ("image-presence", "Image presence"),
            ResidualKind.ExternalId => ("identifier", "External ID"),
            _ => ("residual", "Residual")
        };
        var diagnostic = string.Concat(
            "musicbrainz could not verify ",
            diagnosticFilter,
            " criteria because ",
            reason,
            "; affected candidates were retained.");
        return diagnostic;
    }

    private bool AllMatch(
        string stage,
        SearchHit hit,
        IReadOnlyList<ResidualCriterion> residuals,
        Func<ResidualCriterion, bool> match)
    {
        var accepted = true;
        foreach (var residual in residuals)
        {
            if (!accepted)
            {
                TraceResidual(stage, residual, hit, accepted: true, reason: "prior residual rejected", inputCount: 0);
                continue;
            }

            accepted = match(residual);
            TraceResidual(stage, residual, hit, accepted, accepted ? "evaluated" : "candidate rejected");
        }

        return accepted;
    }

    private void TraceStage(
        string stage,
        string status,
        SearchHit hit,
        int residualCount,
        bool accepted,
        string? reason = null,
        int inputCount = 1)
    {
        var stageReason = reason ??
            (status == "skipped" ? "no residuals declared" : accepted ? "evaluated" : "candidate rejected");
        _currentTrace.Value?.RecordStage(
            _currentStageNodeId.Value ?? _currentNodeId.Value,
            stage + "/residuals",
            status,
            stageReason,
            inputCount,
            accepted ? inputCount : 0);
    }

    private void TraceResidual(
        string stage,
        ResidualCriterion residual,
        SearchHit hit,
        bool accepted,
        string reason,
        int inputCount = 1)
    {
        _currentTrace.Value?.RecordStage(
            _currentStageNodeId.Value ?? _currentNodeId.Value,
            stage + "/" + residual.Kind,
            accepted ? "accepted" : "rejected",
            reason,
            inputCount,
            accepted ? inputCount : 0);
    }

    private void TraceResidualsRetained(string stage, SearchHit hit, IReadOnlyList<ResidualCriterion> residuals)
    {
        foreach (var residual in residuals)
        {
            TraceResidual(stage, residual, hit, accepted: true, reason: "unverifiable retained");
        }
    }

    private void TraceResidualsSkipped(
        string stage,
        SearchHit hit,
        IReadOnlyList<ResidualCriterion> residuals,
        string reason)
    {
        foreach (var residual in residuals)
        {
            TraceResidual(stage, residual, hit, accepted: true, reason: reason, inputCount: 0);
        }
    }

    private void TraceDownstreamSkipped(
        IReadOnlyList<ResidualCriterion> residuals,
        SearchHit hit,
        string rejectedStage)
    {
        if (rejectedStage == "hit")
        {
            var entityResiduals = residuals.Where(residual => residual.Need is
                ResidualNeed.EntityAliases
                or ResidualNeed.EntityUrlRelations
                or ResidualNeed.EntityGenres).ToArray();
            TraceSkippedStage("entity", hit, entityResiduals);
        }

        if (rejectedStage is "hit" or "entity")
        {
            TraceSkippedStage(
                "representative-labels",
                hit,
                Residuals(residuals, ResidualNeed.RepresentativeReleaseLabels));
        }

        if (rejectedStage is "hit" or "entity" or "representative-labels")
        {
            TraceSkippedStage(
                "representative-language",
                hit,
                Residuals(residuals, ResidualNeed.RepresentativeReleaseLanguage));
        }

        if (rejectedStage is "hit" or "entity" or "representative-labels" or "representative-language")
        {
            TraceSkippedStage("cover", hit, Residuals(residuals, ResidualNeed.CoverArt));
        }
    }

    private void TraceSkippedStage(string stage, SearchHit hit, IReadOnlyList<ResidualCriterion> residuals)
    {
        TraceResidualsSkipped(stage, hit, residuals, "prior stage rejected");
        TraceStage(
            stage,
            "skipped",
            hit,
            residuals.Count,
            accepted: true,
            reason: "prior stage rejected",
            inputCount: 0);
    }

    private void TraceHydrationRequested(string stage, SearchHit hit, string need)
    {
        _currentTrace.Value?.RecordStage(
            _currentStageNodeId.Value ?? _currentNodeId.Value,
            stage + "/hydration",
            "requested",
            need,
            1,
            null);
    }

    private void TraceHydrationCompleted(string stage, SearchHit hit, string need, FetchOutcome outcome)
    {
        _currentTrace.Value?.RecordStage(
            _currentStageNodeId.Value ?? _currentNodeId.Value,
            stage + "/hydration",
            outcome.ToString(),
            need,
            1,
            outcome == FetchOutcome.Success ? 1 : 0);
    }

    private sealed record ResolvedArtistComposition
    {
        public required FetchOutcome Outcome { get; init; }

        public IReadOnlyList<ResolvedArtist> Artists { get; init; } = [];

        public IReadOnlyList<string> Warnings { get; init; } = [];
    }

    private sealed record ResolvedArtist
    {
        public required string Mbid { get; init; }

        public string? ArtistType { get; init; }
    }

    private sealed record ExpandedArtistComposition
    {
        public required FetchOutcome Outcome { get; init; }

        public IReadOnlyList<ArtistDto> Artists { get; init; } = [];
    }

    private sealed record ArtistCompositionQuery
    {
        public required IReadOnlyList<string> IncludedArtistMbids { get; init; }

        public required IReadOnlyList<string> ExcludedArtistMbids { get; init; }

        public IReadOnlyList<string> Warnings { get; init; } = [];
    }

    private sealed class VerifiedScanDiagnostics
    {
        public FetchOutcome Outcome { get; set; } = FetchOutcome.Success;

        public bool Truncated { get; set; }

        public IReadOnlyList<string> Warnings { get; set; } = [];
    }
}
