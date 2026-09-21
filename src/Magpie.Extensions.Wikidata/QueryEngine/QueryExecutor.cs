using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.MetadataSearch;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed class QueryExecutor(
    IWikidataClient client,
    IQueryCoordinator cache,
    ILogger<QueryExecutor> logger) : IQueryExecutor
{
    private sealed class QueryTotals
    {
        public int RowsFetched { get; set; }

        public int Requests { get; set; }

        public int PrunedStages { get; set; }

        public int Windows { get; set; }
    }

    private sealed record CandidateWindowResult(
        IReadOnlyList<SearchHit> Hits,
        IReadOnlyList<string> Warnings,
        bool HasMore);

    private sealed record CandidateState(
        SearchHit Hit,
        EntityDto? Entity,
        IReadOnlyList<ResidualFilter> Pending);

    private sealed record CollectionItem(
        object Value,
        string? EntityQid,
        CriteriaMatchResult Existence = CriteriaMatchResult.Matched,
        bool NameComplete = true,
        bool IdentityComplete = true,
        bool BodyComplete = true);

    private sealed record SubjectView(
        IReadOnlyList<string>? Names,
        IReadOnlyList<PropertyValue> ExternalIds);

    [SuppressMessage("Ordering", "SA1201", Justification = "Nested types come first in this codebase.")]
    private const int CandidateLimit = SparqlQueryBuilder.CandidateWindow;

    private const int MaxRequestedLanguages = 50;

    private const int MaxSearchWindows = 20;

    private const int MaxResolvedQids = 500;

    private static readonly IReadOnlyDictionary<string, EntityDto> EmptyEntities =
        new Dictionary<string, EntityDto>();

    private HierarchyRow? MapHierarchyRow(
        IReadOnlyDictionary<string, SparqlBindingDto> binding,
        EntityKind kind)
    {
        var qid = Read(binding, "child").ToQid();
        var title = Read(binding, "childLabel")?.Trim();
        if (qid is null ||
            title is null ||
            !int.TryParse(Read(binding, "ordinal"), out var ordinal) ||
            ordinal <= 0)
        {
            return null;
        }

        var externalIds = new Dictionary<string, string>
        {
            [Constants.ProviderType] = qid
        };
        AddExternal(externalIds, Constants.ImdbSource, Read(binding, "imdbId"));
        AddExternal(externalIds, Constants.TmdbSource, Read(binding, "tmdbId"));
        AddExternal(externalIds, Constants.TvdbSource, Read(binding, "tvdbId"));
        return new HierarchyRow
        {
            EntityKind = kind,
            Qid = qid,
            Ordinal = ordinal,
            Title = title,
            Overview = Read(binding, "childDescription")?.Trim(),
            PublishDate = Read(binding, "publishDate").ToTemporalValue(),
            ImageFilename = Read(binding, "imageFilename").ToCommonsFilename(),
            ExternalIds = externalIds
        };
    }

    private QueryResult Empty(
        QueryPlan plan,
        IReadOnlyList<string> warnings) => new()
        {
            PageInfo = new ProviderSearchPageInfo
            {
                RequestedPage = plan.Page,
                HasMore = false
            },
            Warnings = warnings
        };

    private QueryResult Failure(QueryPlan plan, string diagnostic) =>
        Empty(
            plan,
            [
                .. plan.Warnings,
                diagnostic
            ]);

    private string? Read(IReadOnlyDictionary<string, SparqlBindingDto> binding, string key) =>
        binding.TryGetValue(key, out var value) ? value?.Value : null;

    private bool IsTitleWindowSentinel(IReadOnlyDictionary<string, SparqlBindingDto> binding) =>
        string.Equals(Read(binding, "titleWindowExceeded"), "1", StringComparison.Ordinal);

    private void AddExternal(IDictionary<string, string> values, string source, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[source] = value!.Trim();
        }
    }

    private SubjectFilter? ResolveAlternative(
        SubjectFilter alternative,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resolutions)
    {
        var resolved = alternative with
        {
            Qids =
            [
                .. alternative.Qids
                    .Concat(alternative.Names
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .SelectMany(name => resolutions[name]))
                    .Distinct(StringComparer.Ordinal)
            ],
            Names = []
        };
        if (resolved.Qids.Count > MaxResolvedQids)
        {
            throw new WikidataBudgetException("Wikidata entity resolution matched too many identifiers.");
        }

        return HasSubjectSelector(resolved) ? resolved : null;
    }

    private bool HasSubjectSelector(SubjectFilter alternative) =>
        alternative.Qids.Count > 0 ||
        alternative.UriSchemes.Count > 0 ||
        alternative.UriValues.Count > 0 ||
        alternative.ExternalIds.Count > 0 ||
        alternative.ExternalIdGroups.Any(group => group.Qids.Count > 0 || group.ExternalIds.Count > 0);

    private QueryPlan BindRelationshipSeed(
        QueryPlan plan,
        int relationshipIndex,
        IReadOnlyList<string> qids)
    {
        var filters = plan.Filters.ToArray();
        filters[relationshipIndex] = new QueryFilter
        {
            Kind = QueryFilterKind.ExternalId,
            Alternatives =
            [
                new SubjectFilter
                {
                    Qids = qids
                }
            ]
        };
        return plan with { Filters = filters };
    }

    private int AddDistinct(
        ICollection<SearchHit> destination,
        ISet<string> qids,
        IEnumerable<SearchHit> hits)
    {
        var duplicates = 0;
        foreach (var hit in hits)
        {
            if (qids.Add(hit.Qid))
            {
                destination.Add(hit);
            }
            else
            {
                duplicates++;
            }
        }

        return duplicates;
    }

    private bool HasCandidateProjection(SearchHit hit) => hit.CandidateProjectionApplied;

    private CriteriaMatchResult EvaluateProductionStatus(
        ResidualFilter filter,
        SearchHit hit)
    {
        var criterion = filter.Criterion;
        if (criterion is null ||
            criterion.Operator is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In))
        {
            return CriteriaMatchResult.Unverifiable;
        }

        var requestsEnded = criterion.Values.Any(value => string.Equals(
            value,
            Constants.EndedProductionStatus,
            StringComparison.OrdinalIgnoreCase));
        var requestsNotEnded = criterion.Values.Any(value => !string.Equals(
            value,
            Constants.EndedProductionStatus,
            StringComparison.OrdinalIgnoreCase));
        var matched = hit.HasEndDate ? requestsEnded : requestsNotEnded;
        return ApplyNegation(Result(matched), criterion.Negate);
    }

    private CriteriaMatchResult EvaluateExternalIdGroup(
        ResidualRelationshipFacet facet,
        string subjectQid,
        SubjectView? subject,
        bool supportHydrated)
    {
        if (facet.Qids.Contains(subjectQid, StringComparer.OrdinalIgnoreCase))
        {
            return CriteriaMatchResult.Matched;
        }

        if (facet.ExternalIds.Count == 0)
        {
            return CriteriaMatchResult.Rejected;
        }

        if (!supportHydrated || subject is null)
        {
            return CriteriaMatchResult.Unverifiable;
        }

        return Result(facet.ExternalIds.Any(expected => subject.ExternalIds.Any(actual =>
            string.Equals(actual.PropertyId, expected.PropertyId, StringComparison.Ordinal) &&
            string.Equals(actual.Value, expected.Value, StringComparison.Ordinal))));
    }

    private CriteriaMatchResult ApplyNegation(
        CriteriaMatchResult result,
        bool negate) => !negate ? result : result switch
        {
            CriteriaMatchResult.Matched => CriteriaMatchResult.Rejected,
            CriteriaMatchResult.Rejected => CriteriaMatchResult.Matched,
            _ => CriteriaMatchResult.Unverifiable
        };

    private CriteriaMatchResult CombineAnd(
        CriteriaMatchResult left,
        CriteriaMatchResult right)
    {
        if (left == CriteriaMatchResult.Rejected || right == CriteriaMatchResult.Rejected)
        {
            return CriteriaMatchResult.Rejected;
        }

        return left == CriteriaMatchResult.Unverifiable ||
            right == CriteriaMatchResult.Unverifiable
                ? CriteriaMatchResult.Unverifiable
                : CriteriaMatchResult.Matched;
    }

    private CriteriaMatchResult CombineOr(
        CriteriaMatchResult left,
        CriteriaMatchResult right)
    {
        if (left == CriteriaMatchResult.Matched || right == CriteriaMatchResult.Matched)
        {
            return CriteriaMatchResult.Matched;
        }

        return left == CriteriaMatchResult.Unverifiable ||
            right == CriteriaMatchResult.Unverifiable
                ? CriteriaMatchResult.Unverifiable
                : CriteriaMatchResult.Rejected;
    }

    private CriteriaMatchResult Result(bool matched) => matched
        ? CriteriaMatchResult.Matched
        : CriteriaMatchResult.Rejected;

    private ResidualEvaluationResult Result(
        IReadOnlyList<SearchHit> hits,
        IReadOnlyDictionary<string, HashSet<string>> unverifiableByQid,
        IReadOnlyDictionary<string, int> rejections)
    {
        var activity = Activity.Current;
        if (activity?.IsAllDataRequested == true && rejections.Count > 0)
        {
            var tags = new ActivityTagsCollection();
            foreach (var rejection in rejections)
            {
                tags["magpie.query.rejected." + rejection.Key] = rejection.Value;
            }

            activity.AddEvent(new ActivityEvent("residuals", tags: tags));
        }

        var unverifiableSources = hits.SelectMany(hit => unverifiableByQid.TryGetValue(hit.Qid, out var sources)
            ? sources
            : [])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return new ResidualEvaluationResult
        {
            Hits = hits,
            Warnings = unverifiableSources.Length == 0
                ? []
                :
                [
                    "Wikidata could not fully verify locally evaluated criteria (" +
                            string.Join(", ", unverifiableSources) +
                            "); unverified candidates were retained."
                ]
        };
    }

    private void AddUnverifiable(
        IDictionary<string, HashSet<string>> unverifiableByQid,
        string qid,
        string source)
    {
        if (!unverifiableByQid.TryGetValue(qid, out var sources))
        {
            sources = new HashSet<string>(StringComparer.Ordinal);
            unverifiableByQid[qid] = sources;
        }

        sources.Add(source);
    }

    private RequestContext EntityHydrationContext(
        IReadOnlyList<ResidualFilter> filters,
        RequestContext context)
    {
        if (context.FetchAllLanguages)
        {
            return context;
        }

        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var alternateTextGroups =
            from filter in filters
            let collectionGroup = filter.CollectionGroup
            where collectionGroup?.Kind == ResidualCollectionKind.AlternateText
            select collectionGroup!;
        foreach (var group in alternateTextGroups)
        {
            foreach (var alternative in group.Alternatives)
            {
                var possibleLanguages = PossibleAlternateTextLanguages(alternative);
                if (possibleLanguages is null)
                {
                    return context with { FetchAllLanguages = true };
                }

                required.UnionWith(possibleLanguages);
            }
        }

        if (required.Count == 0)
        {
            return context;
        }

        var languages = context.FallbackLanguages
            .Concat(required.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return languages.Length > MaxRequestedLanguages
            ? context with { FetchAllLanguages = true }
            : context with { FallbackLanguages = languages };
    }

    private HashSet<string>? PossibleAlternateTextLanguages(
        ResidualCollectionAlternative alternative)
    {
        HashSet<string>? possible = null;
        foreach (var predicate in alternative.Predicates.Where(predicate =>
            predicate.Field == ResidualCollectionField.AlternateTextLanguage &&
            !predicate.Negate &&
            predicate.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In))
        {
            var values = predicate.StringValues
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (possible is null)
            {
                possible = values;
            }
            else
            {
                possible.IntersectWith(values);
            }
        }

        return possible;
    }

    private bool IsRateLimited(Exception exception) => exception is HttpRequestException &&
        exception.Data.Contains("wikidata.rate-limited");

    private bool IsQidFallbackLabel(SearchHit hit) => string.Equals(
        hit.Label.Trim(),
        hit.Qid,
        StringComparison.OrdinalIgnoreCase);

    private bool AcceptedType(EntityKind kind, string qid) => kind switch
    {
        EntityKind.Movie => qid is Constants.MovieQid
            or Constants.ShortFilmQid
            or Constants.TelevisionFilmQid,
        EntityKind.Show => qid is Constants.TelevisionSeriesQid
            or Constants.AnimatedSeriesQid,
        _ => false
    };

    public async Task<QueryResult> SearchAsync(
        QueryPlan plan,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        plan = plan.PrepareDiagnostics();
        if (plan.CriterionDispositions.Count > 0)
        {
            logger.LogDebug(
                "Wikidata query resolved criterion dispositions: {Dispositions}",
                string.Join(
                    "; ",
                    plan.CriterionDispositions.Select(
                        disposition => $"{disposition.Source}={disposition.Disposition} ({disposition.Reason})")));
        }

        using var activity = Telemetry.Source.StartActivity("wikidata.query.execute");
        var startedTimestamp = Stopwatch.GetTimestamp();
        var totals = new QueryTotals();
        activity?.SetTag("magpie.query.fingerprint", plan.Fingerprint);
        if (activity?.IsAllDataRequested == true)
        {
            activity.AddEvent(new ActivityEvent(
                "plan",
                tags: new ActivityTagsCollection
                {
                    ["magpie.query.plan_json"] = plan.SerializePlan(),
                    ["magpie.query.anchor_kind"] = plan.Strategy.ToString(),
                    ["magpie.query.known_empty"] = plan.Strategy == QueryStrategy.Empty,
                    ["magpie.query.warnings"] = plan.Warnings.ToArray()
                }));
        }

        if (plan.Strategy == QueryStrategy.Unsupported)
        {
            var unsupported = new QueryResult
            {
                Supported = false,
                PageInfo = ProviderSearchPageInfo.Unsupported
            };
            Complete(
                activity,
                plan,
                totals,
                startedTimestamp,
                TerminalReason.Unbounded,
                survivingRows: 0,
                unsupported);
            return unsupported;
        }

        if (plan.Strategy == QueryStrategy.Empty)
        {
            var empty = Empty(plan, plan.Warnings);
            Complete(
                activity,
                plan,
                totals,
                startedTimestamp,
                plan.CriterionDispositions.Any(value =>
                    value.Disposition == CriterionDispositionKind.ProvablyUnsatisfiable)
                        ? TerminalReason.KnownEmpty
                        : TerminalReason.Unbounded,
                survivingRows: 0,
                empty);
            return empty;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var resolvedPlan = plan;
            resolvedPlan = await ResolveTitleCandidatesAsync(resolvedPlan, context, totals, timeout.Token)
                .ConfigureAwait(false);
            resolvedPlan = await ResolveEntityNamesAsync(resolvedPlan, context, totals, timeout.Token)
                .ConfigureAwait(false);
            plan = resolvedPlan;
            if (plan.Strategy == QueryStrategy.Empty)
            {
                var identityEmpty = Empty(plan, plan.Warnings);
                Telemetry.PlansRefused.Add(
                    1,
                    new KeyValuePair<string, object?>("provider", Telemetry.Provider),
                    new KeyValuePair<string, object?>("content_type", plan.EntityKind.ToString()),
                    new KeyValuePair<string, object?>("reason", TerminalReason.IdentityEmpty));
                Complete(
                    activity,
                    plan,
                    totals,
                    startedTimestamp,
                    TerminalReason.IdentityEmpty,
                    survivingRows: 0,
                    identityEmpty);
                return identityEmpty;
            }

            var logicalOffset = checked((plan.Page - 1) * plan.MaximumResults);
            var logicalBoundary = plan.UsesLocalPaging
                ? checked(logicalOffset + plan.MaximumResults + 1)
                : checked(plan.MaximumResults + 1);
            var collected = new List<SearchHit>();
            var collectedQids = new HashSet<string>(StringComparer.Ordinal);
            var executionWarnings = new List<string>();
            var relationshipIndex = plan.RelationshipSeedFilterIndex();
            if (relationshipIndex >= 0)
            {
                var seedOffset = 0;
                while (collected.Count < logicalBoundary)
                {
                    if (totals.Windows++ == MaxSearchWindows)
                    {
                        throw new WikidataBudgetException("Wikidata search scanned too many pages without finishing.");
                    }

                    var seedRequest = SparqlQueryBuilder.BuildRelationshipSeed(
                        plan.EntityKind,
                        plan.Filters[relationshipIndex],
                        seedOffset);
                    var seedRows = await ExecuteRequestAsync(
                        seedRequest,
                        context,
                        totals,
                        RequestRole.RelationshipSeedWindows,
                        timeout.Token).ConfigureAwait(false);
                    var seedQids = (from row in seedRows
                                    let qid = Read(row, "item").ToQid()
                                    where qid is not null
                                    select qid!)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    var seedHasMore = seedQids.Length > SparqlQueryBuilder.CandidateWindow;
                    var windowQids = seedQids.Take(SparqlQueryBuilder.CandidateWindow).ToArray();
                    if (windowQids.Length == 0)
                    {
                        break;
                    }

                    await CollectCandidateWindowsAsync(
                        BindRelationshipSeed(plan, relationshipIndex, windowQids),
                        context,
                        totals,
                        logicalBoundary,
                        collected,
                        collectedQids,
                        executionWarnings,
                        timeout.Token).ConfigureAwait(false);
                    if (!seedHasMore)
                    {
                        break;
                    }

                    seedOffset += SparqlQueryBuilder.CandidateWindow;
                }
            }
            else
            {
                await CollectCandidateWindowsAsync(
                    plan,
                    context,
                    totals,
                    logicalBoundary,
                    collected,
                    collectedQids,
                    executionWarnings,
                    timeout.Token).ConfigureAwait(false);
            }

            var resultOffset = plan.UsesLocalPaging ? logicalOffset : 0;
            var pageRows = collected.Skip(resultOffset).Take(plan.MaximumResults + 1).ToArray();
            var hasMore = pageRows.Length > plan.MaximumResults;
            var result = new QueryResult
            {
                Hits = [.. pageRows.Take(plan.MaximumResults)],
                PageInfo = new ProviderSearchPageInfo
                {
                    RequestedPage = plan.Page,
                    HasMore = hasMore
                },
                Warnings =
                [
                    .. plan.Warnings
                        .Concat(executionWarnings)
                        .Distinct()
                ]
            };
            Complete(
                activity,
                plan,
                totals,
                startedTimestamp,
                TerminalReason.Completed,
                collected.Count,
                result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Complete(
                activity,
                plan,
                totals,
                startedTimestamp,
                TerminalReason.Cancelled,
                survivingRows: 0,
                result: null);
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var timedOut = Failure(plan, "Wikidata search timed out; no Wikidata results were returned.");
            Complete(
                activity,
                plan,
                totals,
                startedTimestamp,
                TerminalReason.Timeout,
                survivingRows: 0,
                timedOut);
            return timedOut;
        }
        catch (HttpRequestException exception) when (IsRateLimited(exception))
        {
            logger.LogWarning("Wikidata rate limited the search for entity kind {EntityKind}", plan.EntityKind);
            Telemetry.Throttled.Add(
                1,
                new KeyValuePair<string, object?>("provider", Telemetry.Provider),
                new KeyValuePair<string, object?>("kind", TerminalReason.RateLimited));
            var throttled = Failure(plan, "Wikidata search was rate limited; no Wikidata results were returned.");
            Complete(
                activity,
                plan,
                totals,
                startedTimestamp,
                TerminalReason.RateLimited,
                survivingRows: 0,
                throttled);
            return throttled;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            or WikidataBudgetException)
        {
            logger.LogWarning(exception, "Wikidata search failed for entity kind {EntityKind}", plan.EntityKind);
            var failed = Failure(plan, "Wikidata search failed; no Wikidata results were returned.");
            Complete(
                activity,
                plan,
                totals,
                startedTimestamp,
                TerminalReason.Error,
                survivingRows: 0,
                failed);
            return failed;
        }
        catch (Exception)
        {
            Complete(
                activity,
                plan,
                totals,
                startedTimestamp,
                TerminalReason.Error,
                survivingRows: 0,
                result: null);
            throw;
        }
    }

    private async Task CollectCandidateWindowsAsync(
        QueryPlan plan,
        RequestContext context,
        QueryTotals totals,
        int logicalBoundary,
        List<SearchHit> collected,
        HashSet<string> collectedQids,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var candidateOffset = 0;
        while (collected.Count < logicalBoundary)
        {
            if (totals.Windows++ == MaxSearchWindows)
            {
                throw new WikidataBudgetException("Wikidata search scanned too many pages without finishing.");
            }

            var window = await SearchCandidateWindowAsync(
                plan,
                context,
                candidateOffset,
                totals,
                cancellationToken).ConfigureAwait(false);
            AddDistinct(collected, collectedQids, window.Hits);
            warnings.AddRange(window.Warnings);
            if (!plan.UsesLocalPaging || !window.HasMore)
            {
                break;
            }

            candidateOffset += SparqlQueryBuilder.CandidateWindow;
        }
    }

    private async Task<CandidateWindowResult> SearchCandidateWindowAsync(
        QueryPlan plan,
        RequestContext context,
        int candidateOffset,
        QueryTotals totals,
        CancellationToken cancellationToken)
    {
        var rows = await ExecuteRequestAsync(
            SparqlQueryBuilder.BuildSearch(plan, context.Language, candidateOffset),
            context,
            totals,
            RequestRole.WdqsWindows,
            cancellationToken).ConfigureAwait(false);
        totals.RowsFetched += rows.Count;
        var sentinelsRemoved = plan.Strategy == QueryStrategy.TitleSearch
            ? rows.Count(IsTitleWindowSentinel)
            : 0;
        var lookaheadRows = plan.Strategy == QueryStrategy.TitleSearch
            ? sentinelsRemoved
            : plan.UsesLocalPaging
                ? Math.Max(0, rows.Count - SparqlQueryBuilder.CandidateWindow)
                : Math.Max(0, rows.Count - plan.MaximumResults);
        var sourceHasMore = lookaheadRows > 0;
        var visibleRows = plan.Strategy switch
        {
            QueryStrategy.TitleSearch => rows.Where(row => !IsTitleWindowSentinel(row)),
            _ when plan.UsesLocalPaging => rows.Take(SparqlQueryBuilder.CandidateWindow),
            _ => rows
        };
        var hits = (from row in visibleRows
                    let hit = row.ToSearchHit()
                    where hit is not null
                    select hit!).ToArray();
        var filtered = FilterCandidates(plan, hits);
        var verified = new List<SearchHit>();
        var warnings = new List<string>();
        for (var offset = 0; offset < filtered.Count; offset += SparqlQueryBuilder.CandidateWindow)
        {
            var batch = filtered.Skip(offset).Take(SparqlQueryBuilder.CandidateWindow).ToArray();
            var projection = await ProjectCandidatesAsync(
                plan,
                batch,
                context,
                totals,
                cancellationToken)
                .ConfigureAwait(false);
            warnings.AddRange(projection.Warnings);
            if (plan.ResidualFilters.Count == 0 || projection.Hits.Count == 0)
            {
                SkipStage(
                    totals,
                    RequestRole.ResidualEntityHydration,
                    plan.ResidualFilters.Count == 0 ? "no_residual_filters" : "no_candidates");
                verified.AddRange(projection.Hits);
                continue;
            }

            var evaluation = await ApplyResidualsAsync(
                plan,
                projection.Hits,
                context,
                cancellationToken,
                totals).ConfigureAwait(false);
            verified.AddRange(evaluation.Hits);
            warnings.AddRange(evaluation.Warnings);
        }

        return new CandidateWindowResult(verified, warnings, sourceHasMore);
    }

    private async Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>> ExecuteRequestAsync(
        SparqlRequest request,
        RequestContext context,
        QueryTotals totals,
        string role,
        CancellationToken cancellationToken)
    {
        using var activity = StartRequest(role, request.QueryText);
        var provenance = SearchCacheProvenance.Remote;
        SearchCacheResult cached;
        try
        {
            cached = await cache.GetSearchAsync(
                request.CanonicalCacheKey,
                token => client.ExecuteSparqlAsync(request, context, token),
                cancellationToken,
                value => provenance = value).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            or OperationCanceledException or WikidataBudgetException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            EndRequest(activity, totals, role, QueryCoordinatorRegion.Searches, cacheHit: false, rows: 0);
            throw;
        }

        EndRequest(
            activity,
            totals,
            role,
            QueryCoordinatorRegion.Searches,
            provenance != SearchCacheProvenance.Remote,
            cached.Rows.Count);
        return cached.Rows;
    }

    private Activity? StartRequest(string role, string target)
    {
        var activity = Telemetry.Source.StartActivity("wikidata.query.request", ActivityKind.Client);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("magpie.request.role", role);
        if (activity.IsAllDataRequested)
        {
            activity.SetTag("magpie.request.target", target);
        }

        return activity;
    }

    private void EndRequest(
        Activity? activity,
        QueryTotals totals,
        string role,
        string region,
        bool cacheHit,
        int rows)
    {
        if (!cacheHit)
        {
            totals.Requests++;
        }

        activity?.SetTag("magpie.cache.hit", cacheHit);
        activity?.SetTag("magpie.request.rows", rows);
        Telemetry.Requests.Add(
            1,
            new KeyValuePair<string, object?>("provider", Telemetry.Provider),
            new KeyValuePair<string, object?>("request_role", role),
            new KeyValuePair<string, object?>("cache_hit", cacheHit));
        Telemetry.Cache.Add(
            1,
            new KeyValuePair<string, object?>("provider", Telemetry.Provider),
            new KeyValuePair<string, object?>("cache_region", region),
            new KeyValuePair<string, object?>("outcome", cacheHit ? "hit" : "miss"));
    }

    private void SkipStage(QueryTotals totals, string role, string reason)
    {
        totals.PrunedStages++;
        var activity = Activity.Current;
        if (activity?.IsAllDataRequested != true)
        {
            return;
        }

        activity.AddEvent(new ActivityEvent(
            "stage_skipped",
            tags: new ActivityTagsCollection
            {
                ["magpie.request.role"] = role,
                ["magpie.query.skip_reason"] = reason
            }));
    }

    private void Complete(
        Activity? activity,
        QueryPlan plan,
        QueryTotals totals,
        long startedTimestamp,
        string terminalReason,
        int survivingRows,
        QueryResult? result)
    {
        activity?.SetTag("magpie.query.rows_fetched", totals.RowsFetched);
        activity?.SetTag("magpie.query.rows_surviving", survivingRows);
        activity?.SetTag("magpie.query.request_count", totals.Requests);
        activity?.SetTag("magpie.query.pruned_stages", totals.PrunedStages);
        activity?.SetTag("magpie.query.has_more", result?.PageInfo.HasMore ?? false);
        activity?.SetTag("magpie.query.terminal_reason", terminalReason);
        if (terminalReason is TerminalReason.RateLimited
            or TerminalReason.Timeout
            or TerminalReason.Cancelled
            or TerminalReason.Error)
        {
            activity?.SetStatus(ActivityStatusCode.Error, terminalReason);
        }

        Telemetry.Duration.Record(
            (Stopwatch.GetTimestamp() - startedTimestamp) * 1000d / Stopwatch.Frequency,
            new KeyValuePair<string, object?>("provider", Telemetry.Provider),
            new KeyValuePair<string, object?>("content_type", plan.EntityKind.ToString()),
            new KeyValuePair<string, object?>("terminal_reason", terminalReason));
        Telemetry.Results.Record(
            result?.Hits.Count ?? 0,
            new KeyValuePair<string, object?>("provider", Telemetry.Provider),
            new KeyValuePair<string, object?>("content_type", plan.EntityKind.ToString()));
    }

    private async Task<QueryPlan> ResolveTitleCandidatesAsync(
        QueryPlan plan,
        RequestContext context,
        QueryTotals totals,
        CancellationToken cancellationToken)
    {
        if (plan.Strategy != QueryStrategy.TitleSearch || plan.OrdinaryTerm is not null)
        {
            return plan;
        }

        var titleFilter = plan.Filters.First(filter =>
            filter.Kind == QueryFilterKind.Title && !filter.Negate);
        if (titleFilter.Operator != CriteriaValueOperator.StartsWithString)
        {
            return plan;
        }

        var terms = titleFilter.Values;
        var language = context.FallbackLanguages.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? Settings.FallbackLanguage;
        var candidates = new List<string>();
        foreach (var term in terms)
        {
            using var activity = StartRequest(RequestRole.TitleResolution, term);
            var provenance = SearchCacheProvenance.Remote;
            try
            {
                var key = language.Trim().ToLowerInvariant() + "|title|" + term.Trim().ToLowerInvariant();
                var resolved = await cache.GetEntitySearchAsync(
                    key,
                    token => client.SearchEntitiesAsync(term, context, token),
                    cancellationToken,
                    value => provenance = value).ConfigureAwait(false);
                candidates.AddRange(resolved);
                EndRequest(
                    activity,
                    totals,
                    RequestRole.TitleResolution,
                    QueryCoordinatorRegion.EntitySearches,
                    provenance != SearchCacheProvenance.Remote,
                    resolved.Count);
            }
            catch (Exception exception) when (exception is JsonException or HttpRequestException
                or WikidataBudgetException ||
                (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                EndRequest(
                    activity,
                    totals,
                    RequestRole.TitleResolution,
                    QueryCoordinatorRegion.EntitySearches,
                    cacheHit: false,
                    rows: 0);
                logger.LogWarning(exception, "Wikidata title candidate lookup failed for {Term}", term);
            }
        }

        return plan with
        {
            TitleCandidateQids = [.. candidates.Distinct(StringComparer.Ordinal)]
        };
    }

    private async Task<QueryPlan> ResolveEntityNamesAsync(
        QueryPlan plan,
        RequestContext context,
        QueryTotals totals,
        CancellationToken cancellationToken)
    {
        var names = (from filter in plan.Filters
                     from alternative in filter.Alternatives
                     from name in alternative.Names
                     where !string.IsNullOrWhiteSpace(name)
                     select name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length == 0)
        {
            return plan;
        }

        var language = context.FallbackLanguages.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? Settings.FallbackLanguage;
        var resolutions = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var key = language.Trim().ToLowerInvariant() + "|" + name.Trim().ToLowerInvariant();
            using var activity = StartRequest(RequestRole.EntityResolution, name);
            var provenance = SearchCacheProvenance.Remote;
            IReadOnlyList<string> resolved;
            try
            {
                resolved = await cache.GetEntitySearchAsync(
                    key,
                    token => client.SearchEntitiesAsync(name, context, token),
                    cancellationToken,
                    value => provenance = value).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                EndRequest(
                    activity,
                    totals,
                    RequestRole.EntityResolution,
                    QueryCoordinatorRegion.EntitySearches,
                    cacheHit: false,
                    rows: 0);
                throw;
            }

            resolutions[name] = resolved;
            EndRequest(
                activity,
                totals,
                RequestRole.EntityResolution,
                QueryCoordinatorRegion.EntitySearches,
                provenance != SearchCacheProvenance.Remote,
                resolved.Count);
        }

        var resolvedFilters = new List<QueryFilter>();
        foreach (var filter in plan.Filters)
        {
            var alternatives = (from alternative in filter.Alternatives
                                let resolved = alternative.Names.Count == 0
                                    ? alternative
                                    : ResolveAlternative(alternative, resolutions)
                                where resolved is not null
                                select resolved!).ToArray();
            if (filter.Alternatives.Count > 0 && alternatives.Length == 0)
            {
                if (filter.Negate)
                {
                    continue;
                }

                return plan with
                {
                    Strategy = QueryStrategy.Empty,
                    Filters = []
                };
            }

            resolvedFilters.Add(filter with { Alternatives = alternatives });
        }

        return plan with { Filters = resolvedFilters };
    }

    private async Task<(
        IReadOnlyList<SearchHit> Hits,
        IReadOnlyList<string> Warnings)> ProjectCandidatesAsync(
            QueryPlan plan,
            IReadOnlyList<SearchHit> hits,
            RequestContext context,
            QueryTotals totals,
            CancellationToken cancellationToken)
    {
        var alreadyProjected = hits.Count > 0 && hits.All(HasCandidateProjection);
        if (hits.Count == 0 ||
            !plan.ProjectCandidateDetails ||
            alreadyProjected)
        {
            SkipStage(
                totals,
                RequestRole.CandidateProjection,
                hits.Count == 0
                    ? "no_candidates"
                    : !plan.ProjectCandidateDetails ? "not_requested" : "already_projected");
            return (hits, []);
        }

        var request = SparqlQueryBuilder.BuildCandidateProjection(
            plan.EntityKind,
            [.. hits.Select(hit => hit.Qid)],
            context.Language);
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows;
        try
        {
            rows = await ExecuteRequestAsync(
                request,
                context,
                totals,
                RequestRole.CandidateProjection,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (
                hits,
                [
                    "Wikidata result details timed out; some Wikidata result details are missing."
                ]);
        }
        catch (HttpRequestException exception) when (IsRateLimited(exception))
        {
            return (
                hits,
                [
                    "Wikidata result details were rate limited; some Wikidata result details are missing."
                ]);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            or WikidataBudgetException)
        {
            logger.LogWarning(
                exception,
                "Wikidata result detail projection failed for entity kind {EntityKind}",
                plan.EntityKind);
            return (
                hits,
                [
                    "Wikidata result details failed to load; some Wikidata result details are missing."
                ]);
        }

        var projections = (from row in rows
                           let qid = Read(row, "item").ToQid()
                           where qid is not null
                           group row by qid! into matches
                           select matches)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return (
            [.. hits.Select(hit => projections.TryGetValue(hit.Qid, out var row)
                ? hit.ApplyProjection(row)
                : hit)],
            []);
    }

    public async Task<HierarchyResult> GetHierarchyAsync(
        EntityKind childKind,
        string parentQid,
        string? showQid,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var request = SparqlQueryBuilder.BuildHierarchy(childKind, parentQid, showQid, context.Language);
        return await cache.GetHierarchyAsync(
            request.CanonicalCacheKey,
            async token =>
            {
                var rows = await client.ExecuteSparqlAsync(request, context, token).ConfigureAwait(false);
                var mapped = new List<HierarchyRow>();
                foreach (var row in rows)
                {
                    var hierarchyRow = MapHierarchyRow(row, childKind);
                    if (hierarchyRow is not null)
                    {
                        mapped.Add(hierarchyRow);
                    }
                }

                var truncated = mapped.Count > SparqlQueryBuilder.HierarchyLimit;
                return new HierarchyResult
                {
                    Rows = [.. mapped.Take(SparqlQueryBuilder.HierarchyLimit)],
                    Truncated = truncated
                };
            },
            cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<SearchHit> FilterCandidates(QueryPlan plan, IReadOnlyList<SearchHit> hits)
    {
        var results = new List<SearchHit>(hits.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hit in hits)
        {
            if (!AcceptedType(plan.EntityKind, hit.TypeQid) ||
                string.IsNullOrWhiteSpace(hit.Qid) ||
                string.IsNullOrWhiteSpace(hit.Label) ||
                IsQidFallbackLabel(hit) ||
                !seen.Add(hit.Qid) ||
                !MatchesTitles(plan.Filters, hit.Label))
            {
                continue;
            }

            results.Add(hit);
        }

        return results;
    }

    private bool MatchesTitles(IReadOnlyList<QueryFilter> filters, string title)
    {
        foreach (var filter in filters)
        {
            if (filter.Kind != QueryFilterKind.Title)
            {
                continue;
            }

            if (CriteriaMatcher.Match(title, filter.Operator, filter.Values, filter.Negate) !=
                CriteriaMatchResult.Matched)
            {
                return false;
            }
        }

        return true;
    }

    private Task<ResidualHydrationBatch> HydrateCandidatesAsync(
        IEnumerable<string> qids,
        RequestContext context,
        CancellationToken cancellationToken,
        Action<SearchCacheProvenance>? provenanceObserver = null)
    {
        return HydrateAsync(qids, CandidateLimit, context, cancellationToken, provenanceObserver);
    }

    private Task<ResidualHydrationBatch> HydrateSupportAsync(
        IEnumerable<string> qids,
        RequestContext context,
        CancellationToken cancellationToken,
        Action<SearchCacheProvenance>? provenanceObserver = null)
    {
        return HydrateAsync(qids, null, context, cancellationToken, provenanceObserver);
    }

    private async Task<ResidualHydrationBatch> HydrateAsync(
        IEnumerable<string> qids,
        int? limit,
        RequestContext context,
        CancellationToken cancellationToken,
        Action<SearchCacheProvenance>? provenanceObserver)
    {
        var distinct = qids.Where(qid => !string.IsNullOrWhiteSpace(qid))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var requested = limit is { } maximum ? [.. distinct.Take(maximum)] : distinct;
        if (requested.Length == 0)
        {
            return new ResidualHydrationBatch
            {
                Entities = new Dictionary<string, EntityDto>(),
                RequestedCount = 0,
                Truncated = distinct.Length > 0
            };
        }

        try
        {
            var entities = await cache
                .GetEntitiesAsync(requested, context, cancellationToken, provenanceObserver)
                .ConfigureAwait(false);
            return new ResidualHydrationBatch
            {
                Entities = entities,
                RequestedCount = requested.Length,
                Truncated = requested.Length < distinct.Length
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            or OperationCanceledException or WikidataBudgetException)
        {
            return new ResidualHydrationBatch
            {
                Entities = new Dictionary<string, EntityDto>(),
                RequestedCount = requested.Length,
                Failed = true,
                Truncated = requested.Length < distinct.Length
            };
        }
    }

    private async Task<ResidualEvaluationResult> ApplyResidualsAsync(
        QueryPlan plan,
        IReadOnlyList<SearchHit> hits,
        RequestContext context,
        CancellationToken cancellationToken,
        QueryTotals totals)
    {
        var unverifiableByQid = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var rejections = new Dictionary<string, int>(StringComparer.Ordinal);
        var survivors = ApplyHitResiduals(plan.ResidualFilters, hits, unverifiableByQid, rejections);
        if (survivors.Count == 0)
        {
            SkipStage(totals, RequestRole.ResidualEntityHydration, "no_survivors");
            return Result(survivors, unverifiableByQid, rejections);
        }

        var entityResiduals = plan.ResidualFilters
            .Where(filter => filter.Hydration.HasFlag(ResidualHydration.Entity))
            .ToArray();
        var hasSupportResiduals = entityResiduals.Any(filter =>
            filter.Hydration.HasFlag(ResidualHydration.Support));
        if (entityResiduals.Length == 0)
        {
            SkipStage(totals, RequestRole.ResidualEntityHydration, "no_entity_residuals");
            return Result(survivors, unverifiableByQid, rejections);
        }

        var entityContext = EntityHydrationContext(entityResiduals, context);
        var entityQids = survivors.Select(hit => hit.Qid).ToArray();
        var entityRemote = false;
        ResidualHydrationBatch entityBatch;
        using (var entityActivity = StartRequest(
            RequestRole.ResidualEntityHydration,
            string.Join("|", entityQids)))
        {
            entityBatch = await HydrateCandidatesAsync(
                entityQids,
                entityContext,
                cancellationToken,
                value => entityRemote |= value == SearchCacheProvenance.Remote).ConfigureAwait(false);
            if (entityBatch.Failed)
            {
                entityActivity?.SetStatus(ActivityStatusCode.Error, "hydration_failed");
            }

            EndRequest(
                entityActivity,
                totals,
                RequestRole.ResidualEntityHydration,
                QueryCoordinatorRegion.Entities,
                !entityRemote,
                entityBatch.Entities.Count);
        }

        if (entityBatch.Failed)
        {
            foreach (var hit in survivors)
            {
                foreach (var filter in entityResiduals)
                {
                    AddUnverifiable(unverifiableByQid, hit.Qid, filter.Source);
                }
            }

            return Result(survivors, unverifiableByQid, rejections);
        }

        var states = ApplyEntityResiduals(
            plan,
            survivors,
            entityResiduals,
            entityBatch.Entities,
            context.Language,
            unverifiableByQid,
            rejections);
        if (states.Count == 0)
        {
            return Result([], unverifiableByQid, rejections);
        }

        if (!hasSupportResiduals)
        {
            SkipStage(totals, RequestRole.ResidualSupportHydration, "no_support_residuals");
            return Result([.. states.Select(state => state.Hit)], unverifiableByQid, rejections);
        }

        var supportStates = states.Where(state => state.Pending.Count > 0).ToArray();
        if (supportStates.Length == 0)
        {
            SkipStage(totals, RequestRole.ResidualSupportHydration, "nothing_pending");
            return Result([.. states.Select(state => state.Hit)], unverifiableByQid, rejections);
        }

        var supportQids = supportStates
            .SelectMany(state => RequiredSupportQids(state.Entity!, state.Pending))
            .Concat(supportStates.SelectMany(state => RequiredDeferredSupportQids(state.Entity!, state.Pending)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ResidualHydrationBatch supportBatch;
        if (supportQids.Length == 0)
        {
            SkipStage(totals, RequestRole.ResidualSupportHydration, "no_support_qids");
            supportBatch = new ResidualHydrationBatch
            {
                Entities = EmptyEntities,
                RequestedCount = 0
            };
        }
        else
        {
            var supportRemote = false;
            using var supportActivity = StartRequest(
                RequestRole.ResidualSupportHydration,
                string.Join("|", supportQids));
            supportBatch = await HydrateSupportAsync(
                supportQids,
                context,
                cancellationToken,
                value => supportRemote |= value == SearchCacheProvenance.Remote).ConfigureAwait(false);
            if (supportBatch.Failed)
            {
                supportActivity?.SetStatus(ActivityStatusCode.Error, "hydration_failed");
            }

            EndRequest(
                supportActivity,
                totals,
                RequestRole.ResidualSupportHydration,
                QueryCoordinatorRegion.Entities,
                !supportRemote,
                supportBatch.Entities.Count);
        }

        if (supportBatch.Failed)
        {
            foreach (var state in supportStates)
            {
                foreach (var filter in state.Pending)
                {
                    AddUnverifiable(unverifiableByQid, state.Hit.Qid, filter.Source);
                }
            }

            return Result([.. states.Select(state => state.Hit)], unverifiableByQid, rejections);
        }

        var finalStates = ApplySupportResiduals(
            plan,
            states,
            supportBatch.Entities,
            context.Language,
            unverifiableByQid,
            rejections);
        return Result([.. finalStates.Select(state => state.Hit)], unverifiableByQid, rejections);
    }

    private IReadOnlyList<SearchHit> ApplyHitResiduals(
        IReadOnlyList<ResidualFilter> filters,
        IReadOnlyList<SearchHit> hits,
        IDictionary<string, HashSet<string>> unverifiableByQid,
        IDictionary<string, int> rejections)
    {
        var survivors = hits.ToList();
        foreach (var filter in filters.Where(filter => filter.Hydration == ResidualHydration.None))
        {
            var before = survivors.Count;
            var next = new List<SearchHit>(before);
            foreach (var hit in survivors)
            {
                var result = EvaluateHit(filter, hit);
                if (result != CriteriaMatchResult.Rejected)
                {
                    next.Add(hit);
                }

                if (result == CriteriaMatchResult.Unverifiable)
                {
                    AddUnverifiable(unverifiableByQid, hit.Qid, filter.Source);
                }
            }

            survivors = next;
            rejections[filter.Source] =
                (rejections.TryGetValue(filter.Source, out var rejected) ? rejected : 0) +
                before - survivors.Count;
            if (survivors.Count == 0)
            {
                break;
            }
        }

        return survivors;
    }

    private List<CandidateState> ApplyEntityResiduals(
        QueryPlan plan,
        IReadOnlyList<SearchHit> hits,
        IReadOnlyList<ResidualFilter> filters,
        IReadOnlyDictionary<string, EntityDto> entities,
        string language,
        IDictionary<string, HashSet<string>> unverifiableByQid,
        IDictionary<string, int> rejections)
    {
        var states = hits.Select(hit => entities.TryGetValue(hit.Qid, out var entity) && !entity.Missing
            ? new CandidateState(hit, entity, [])
            : new CandidateState(hit, null, []))
            .ToList();
        foreach (var filter in filters)
        {
            var before = states.Count;
            var next = new List<CandidateState>(before);
            foreach (var state in states)
            {
                if (state.Entity is null)
                {
                    AddUnverifiable(unverifiableByQid, state.Hit.Qid, filter.Source);
                    next.Add(state);
                    continue;
                }

                var result = EvaluateEntity(
                    plan,
                    filter,
                    state.Hit,
                    state.Entity,
                    EmptyEntities,
                    language,
                    supportHydrated: false);
                if (result == CriteriaMatchResult.Rejected)
                {
                    continue;
                }

                if (result == CriteriaMatchResult.Unverifiable)
                {
                    if (filter.Hydration.HasFlag(ResidualHydration.Support))
                    {
                        next.Add(new CandidateState(state.Hit, state.Entity, [.. state.Pending, filter]));
                        continue;
                    }

                    AddUnverifiable(unverifiableByQid, state.Hit.Qid, filter.Source);
                }

                next.Add(state);
            }

            states = next;
            rejections[filter.Source] =
                (rejections.TryGetValue(filter.Source, out var rejected) ? rejected : 0) +
                before - states.Count;
            if (states.Count == 0)
            {
                break;
            }
        }

        return states;
    }

    private List<CandidateState> ApplySupportResiduals(
        QueryPlan plan,
        IReadOnlyList<CandidateState> states,
        IReadOnlyDictionary<string, EntityDto> support,
        string language,
        IDictionary<string, HashSet<string>> unverifiableByQid,
        IDictionary<string, int> rejections)
    {
        var result = states.ToList();
        var filters = plan.ResidualFilters
            .Where(filter => filter.Hydration.HasFlag(ResidualHydration.Support))
            .ToArray();
        foreach (var filter in filters)
        {
            var before = result.Count;
            var next = new List<CandidateState>(before);
            foreach (var state in result)
            {
                if (state.Entity is null || !state.Pending.Contains(filter))
                {
                    next.Add(state);
                    continue;
                }

                var match = EvaluateEntity(
                    plan,
                    filter,
                    state.Hit,
                    state.Entity,
                    support,
                    language,
                    supportHydrated: true);
                if (match == CriteriaMatchResult.Rejected)
                {
                    continue;
                }

                if (match == CriteriaMatchResult.Unverifiable)
                {
                    AddUnverifiable(unverifiableByQid, state.Hit.Qid, filter.Source);
                }

                next.Add(new CandidateState(
                    state.Hit,
                    state.Entity,
                    [.. state.Pending.Where(value => !ReferenceEquals(value, filter))]));
            }

            result = next;
            rejections[filter.Source] =
                (rejections.TryGetValue(filter.Source, out var rejected) ? rejected : 0) +
                before - result.Count;
            if (result.Count == 0)
            {
                break;
            }
        }

        return result;
    }

    private CriteriaMatchResult EvaluateHit(ResidualFilter filter, SearchHit hit) =>
        filter.Kind switch
        {
            ResidualFilterKind.Text => EvaluateText(
                filter,
                hit,
                entity: null,
                kind: default,
                language: null,
                EmptyEntities,
                supportHydrated: false),
            ResidualFilterKind.ProductionStatus => EvaluateProductionStatus(filter, hit),
            ResidualFilterKind.Collection => EvaluateHitCollection(filter, hit),
            _ => CriteriaMatchResult.Unverifiable
        };

    private CriteriaMatchResult EvaluateHitCollection(
        ResidualFilter filter,
        SearchHit hit)
    {
        var group = filter.CollectionGroup;
        if (group is null || group.Kind != ResidualCollectionKind.Genre)
        {
            return CriteriaMatchResult.Unverifiable;
        }

        var items = hit.Genres.Select(value => new CollectionItem(value, EntityQid: null)).ToArray();
        var result = EvaluateCollectionAlternatives(group.Alternatives, items);
        return ApplyNegation(result, group.OuterNegate);
    }

    private CriteriaMatchResult EvaluateEntity(
        QueryPlan plan,
        ResidualFilter filter,
        SearchHit hit,
        EntityDto entity,
        IReadOnlyDictionary<string, EntityDto> support,
        string language,
        bool supportHydrated) => filter.Kind switch
        {
            ResidualFilterKind.Text => EvaluateText(
                filter,
                hit,
                entity,
                plan.EntityKind,
                language,
                support,
                supportHydrated),
            ResidualFilterKind.Language => EvaluateLanguage(
                filter,
                entity,
                support,
                language,
                supportHydrated),
            ResidualFilterKind.Relationship => EvaluateRelationship(
                filter,
                entity,
                support,
                language,
                supportHydrated),
            ResidualFilterKind.Collection => EvaluateCollection(
                plan,
                filter,
                entity,
                support,
                language,
                supportHydrated),
            _ => CriteriaMatchResult.Unverifiable
        };

    private CriteriaMatchResult EvaluateText(
        ResidualFilter filter,
        SearchHit hit,
        EntityDto? entity,
        EntityKind kind,
        string? language,
        IReadOnlyDictionary<string, EntityDto> support,
        bool supportHydrated)
    {
        if (filter.Criterion is null || filter.TextField is null)
        {
            return CriteriaMatchResult.Unverifiable;
        }

        string? actual;
        if (filter.TextField == ResidualTextField.OriginalTitle && entity is not null)
        {
            if (filter.Hydration.HasFlag(ResidualHydration.Support) && !supportHydrated)
            {
                return CriteriaMatchResult.Unverifiable;
            }

            var languageQids = entity.EntityClaimQids(Constants.OriginalLanguagePropertyId);
            if (languageQids.Any(qid => !support.ContainsKey(qid)))
            {
                return CriteriaMatchResult.Unverifiable;
            }

            actual = entity.ToMetadataContribution(
                kind,
                string.IsNullOrEmpty(language) ? Settings.FallbackLanguage : language,
                support,
                DateTime.UnixEpoch)?.OriginalTitle;
        }
        else
        {
            actual = filter.TextField switch
            {
                ResidualTextField.Title => hit.Label,
                ResidualTextField.Overview => hit.Description,
                _ => null
            };
        }

        return CriteriaMatcher.Match(
            actual,
            filter.Criterion.Operator,
            filter.Criterion.Values,
            filter.Criterion.Negate);
    }

    private CriteriaMatchResult EvaluateLanguage(
        ResidualFilter filter,
        EntityDto entity,
        IReadOnlyDictionary<string, EntityDto> support,
        string languagePreference,
        bool supportHydrated)
    {
        if (filter.Criterion is null)
        {
            return CriteriaMatchResult.Unverifiable;
        }

        var languageQids = entity.EntityClaimQids(Constants.OriginalLanguagePropertyId);
        if (languageQids.Count == 0)
        {
            return CriteriaMatchResult.Unverifiable;
        }

        var aggregate = CriteriaMatchResult.Rejected;
        foreach (var qid in languageQids)
        {
            if (filter.LanguageQids.Contains(qid, StringComparer.OrdinalIgnoreCase))
            {
                aggregate = CriteriaMatchResult.Matched;
                break;
            }

            if (filter.LanguageCodes.Count == 0 && filter.LanguageNames.Count == 0)
            {
                continue;
            }

            if (!supportHydrated || !support.TryGetValue(qid, out var language) || language.Missing)
            {
                aggregate = CombineOr(aggregate, CriteriaMatchResult.Unverifiable);
                continue;
            }

            var codeMatch = language.StringClaimValues(Constants.LanguageCodePropertyId)
                .Any(code => filter.LanguageCodes.Contains(code, StringComparer.OrdinalIgnoreCase));
            var nameMatch = language.Names(languagePreference)
                .Any(name => filter.LanguageNames.Contains(name, StringComparer.OrdinalIgnoreCase));
            aggregate = CombineOr(aggregate, Result(codeMatch || nameMatch));
        }

        return ApplyNegation(aggregate, filter.Criterion.Negate);
    }

    private CriteriaMatchResult EvaluateRelationship(
        ResidualFilter filter,
        EntityDto entity,
        IReadOnlyDictionary<string, EntityDto> support,
        string language,
        bool supportHydrated)
    {
        if (filter.RelationshipGroup is null)
        {
            return CriteriaMatchResult.Unverifiable;
        }

        var pending = entity.ToEvaluationRelationships();
        var groupResult = CriteriaMatchResult.Rejected;
        foreach (var alternative in filter.RelationshipGroup.Alternatives)
        {
            var alternativeResult = CriteriaMatchResult.Rejected;
            foreach (var relationship in pending.Where(value => alternative.PropertyIds.Contains(
                value.PropertyId,
                StringComparer.Ordinal)))
            {
                var relationshipResult = EvaluateRelationshipAlternative(
                    alternative,
                    relationship,
                    support,
                    language,
                    supportHydrated);
                alternativeResult = CombineOr(alternativeResult, relationshipResult);
                if (alternativeResult == CriteriaMatchResult.Matched)
                {
                    break;
                }
            }

            groupResult = CombineOr(groupResult, alternativeResult);
            if (groupResult == CriteriaMatchResult.Matched)
            {
                break;
            }
        }

        return ApplyNegation(groupResult, filter.RelationshipGroup.OuterNegate);
    }

    private CriteriaMatchResult EvaluateRelationshipAlternative(
        ResidualRelationshipAlternative alternative,
        PendingRelationship relationship,
        IReadOnlyDictionary<string, EntityDto> support,
        string language,
        bool supportHydrated)
    {
        SubjectView? subject = null;
        var needsSubjectData = alternative.Subjects.Any(value => value.Names.Count > 0) ||
            alternative.ExternalIdGroups.Any(group => group.ExternalIds.Count > 0);
        if (supportHydrated && needsSubjectData)
        {
            if (!support.TryGetValue(relationship.SubjectQid, out var subjectEntity) || subjectEntity.Missing)
            {
                return CriteriaMatchResult.Unverifiable;
            }

            subject = MapSubject(subjectEntity, language);
        }

        var result = CriteriaMatchResult.Matched;
        foreach (var subjectFacet in alternative.Subjects)
        {
            result = CombineAnd(
                result,
                EvaluateRelationshipSubject(
                    subjectFacet,
                    relationship,
                    subject,
                    supportHydrated));
        }

        foreach (var qualifier in alternative.Qualifiers)
        {
            result = CombineAnd(
                result,
                EvaluateRelationshipQualifier(qualifier, relationship, support, language, supportHydrated));
        }

        foreach (var externalGroup in alternative.ExternalIdGroups)
        {
            result = CombineAnd(
                result,
                EvaluateExternalIdGroup(externalGroup, relationship.SubjectQid, subject, supportHydrated));
        }

        return result;
    }

    private CriteriaMatchResult EvaluateCollection(
        QueryPlan plan,
        ResidualFilter filter,
        EntityDto entity,
        IReadOnlyDictionary<string, EntityDto> support,
        string language,
        bool supportHydrated)
    {
        var group = filter.CollectionGroup;
        if (group is null)
        {
            return CriteriaMatchResult.Unverifiable;
        }

        var needsSupport = group.Kind is ResidualCollectionKind.ContentRating
            or ResidualCollectionKind.Award;
        if (needsSupport && !supportHydrated)
        {
            return CriteriaMatchResult.Unverifiable;
        }

        var contribution = entity.ToMetadataContribution(
            plan.EntityKind,
            language,
            support,
            DateTime.UnixEpoch);
        if (contribution is null)
        {
            return CriteriaMatchResult.Unverifiable;
        }

        var items = CollectionItems(group.Kind, contribution, entity, support, language);
        var result = EvaluateCollectionAlternatives(group.Alternatives, items);
        return ApplyNegation(result, group.OuterNegate);
    }

    private CriteriaMatchResult EvaluateCollectionAlternatives(
        IReadOnlyList<ResidualCollectionAlternative> alternatives,
        IReadOnlyList<CollectionItem> items)
    {
        var result = CriteriaMatchResult.Rejected;
        foreach (var alternative in alternatives)
        {
            var alternativeResult = CriteriaMatchResult.Rejected;
            foreach (var item in items)
            {
                var itemResult = item.Existence;
                foreach (var predicate in alternative.Predicates)
                {
                    itemResult = CombineAnd(itemResult, EvaluateCollectionPredicate(predicate, item));
                }

                alternativeResult = CombineOr(alternativeResult, itemResult);
                if (alternativeResult == CriteriaMatchResult.Matched)
                {
                    break;
                }
            }

            result = CombineOr(result, alternativeResult);
            if (result == CriteriaMatchResult.Matched)
            {
                break;
            }
        }

        return result;
    }

    private CriteriaMatchResult EvaluateCollectionPredicate(
        ResidualCollectionPredicate predicate,
        CollectionItem item) => (predicate.Field, item.Value) switch
        {
            (ResidualCollectionField.ContentRatingBody, ContentRating value) =>
                MatchKnownString(value.Body, predicate),
            (ResidualCollectionField.ContentRatingRating, ContentRating value) =>
                MatchEntityName(
                    value.Rating,
                    item.EntityQid,
                    predicate,
                    item.NameComplete,
                    item.IdentityComplete),
            (ResidualCollectionField.AwardBody, Award value) =>
                MatchKnownString(value.Body, predicate, item.BodyComplete),
            (ResidualCollectionField.AwardName, Award value) =>
                MatchEntityName(
                    value.Name,
                    item.EntityQid,
                    predicate,
                    item.NameComplete,
                    item.IdentityComplete),
            (ResidualCollectionField.AwardCategory, Award value) =>
                MatchKnownString(value.Category, predicate),
            (ResidualCollectionField.AwardStatus, Award value) =>
                MatchKnownString(value.Status, predicate),
            (ResidualCollectionField.AwardYear, Award value) =>
                MatchKnownInteger(value.Year, predicate),
            (ResidualCollectionField.AwardLanguage, Award value) =>
                MatchKnownString(value.Language, predicate),
            (ResidualCollectionField.AwardCountry, Award value) =>
                MatchKnownString(value.Country, predicate),
            (ResidualCollectionField.ImageType, Image value) =>
                MatchKnownString(value.Type, predicate),
            (ResidualCollectionField.ImageMimeType, Image value) =>
                MatchKnownString(value.MimeType, predicate),
            (ResidualCollectionField.ImageUrl, Image value) =>
                MatchKnownString(value.Url.AbsoluteUri, predicate),
            (ResidualCollectionField.ImageSource, Image value) =>
                MatchKnownString(value.Source, predicate),
            (ResidualCollectionField.ImageLanguage, Image value) =>
                MatchKnownString(value.Language, predicate),
            (ResidualCollectionField.ImageWidth, Image value) =>
                MatchKnownInteger(value.Width, predicate),
            (ResidualCollectionField.ImageHeight, Image value) =>
                MatchKnownInteger(value.Height, predicate),
            (ResidualCollectionField.ReferenceOrdinal, Reference value) =>
                MatchKnownInteger(value.Ordinal, predicate),
            (ResidualCollectionField.ReferenceKind, Reference value) =>
                MatchKnownString(value.Kind, predicate),
            (ResidualCollectionField.ReferenceUrl, Reference value) => MatchUri(value.Url, predicate),
            (ResidualCollectionField.ReferenceDisplayName, Reference value) =>
                MatchKnownString(value.DisplayName, predicate),
            (ResidualCollectionField.ReferenceLanguage, Reference value) =>
                MatchKnownString(value.Language, predicate),
            (ResidualCollectionField.PreviewMimeType, Preview value) =>
                MatchKnownString(value.MimeType, predicate),
            (ResidualCollectionField.PreviewUrl, Preview value) =>
                MatchKnownString(value.Url.AbsoluteUri, predicate),
            (ResidualCollectionField.PreviewSource, Preview value) =>
                MatchKnownString(value.Source, predicate),
            (ResidualCollectionField.PreviewTitle, Preview value) =>
                MatchKnownString(value.Title, predicate),
            (ResidualCollectionField.PreviewLanguage, Preview value) =>
                MatchKnownString(value.Language, predicate),
            (ResidualCollectionField.AlternateTextKind, AlternateText value) =>
                MatchKnownString(value.Kind, predicate),
            (ResidualCollectionField.AlternateTextLanguage, AlternateText value) =>
                MatchKnownString(value.Language, predicate),
            (ResidualCollectionField.AlternateTextText, AlternateText value) =>
                MatchKnownString(value.Text, predicate),
            (ResidualCollectionField.GenreName, string value) =>
                MatchKnownString(value, predicate),
            _ => CriteriaMatchResult.Unverifiable
        };

    private CriteriaMatchResult MatchEntityName(
        string actual,
        string? entityQid,
        ResidualCollectionPredicate predicate,
        bool nameComplete = true,
        bool identityComplete = true)
    {
        var interpreted = CriteriaMatcher.InterpretOperator(predicate.Operator, predicate.Negate);
        if (interpreted.Operator is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In) ||
            predicate.EntityQids.Count == 0)
        {
            return MatchKnownString(actual, predicate, nameComplete);
        }

        var qidMatches = entityQid is not null &&
            predicate.EntityQids.Contains(entityQid, StringComparer.OrdinalIgnoreCase);
        var qidResult = !qidMatches
            ? CriteriaMatchResult.Rejected
            : identityComplete
                ? CriteriaMatchResult.Matched
                : CriteriaMatchResult.Unverifiable;
        var nameResult = predicate.StringValues.Count == 0
            ? CriteriaMatchResult.Rejected
            : !nameComplete
                ? CriteriaMatchResult.Unverifiable
                : CriteriaMatcher.Match(actual, interpreted.Operator, predicate.StringValues);
        return ApplyNegation(CombineOr(qidResult, nameResult), interpreted.Negate);
    }

    private CriteriaMatchResult MatchKnownString(
        string? actual,
        ResidualCollectionPredicate predicate,
        bool complete = true)
    {
        var interpreted = CriteriaMatcher.InterpretOperator(predicate.Operator, predicate.Negate);
        if (!complete)
        {
            var incompleteResult = interpreted.Operator switch
            {
                CriteriaValueOperator.IsNull => CriteriaMatchResult.Rejected,
                CriteriaValueOperator.IsNotNull => CriteriaMatchResult.Matched,
                _ => CriteriaMatchResult.Unverifiable
            };
            return ApplyNegation(incompleteResult, interpreted.Negate);
        }

        if (actual is not null)
        {
            return CriteriaMatcher.Match(actual, interpreted.Operator, predicate.StringValues, interpreted.Negate);
        }

        var result = interpreted.Operator switch
        {
            CriteriaValueOperator.IsNull => CriteriaMatchResult.Matched,
            CriteriaValueOperator.IsNotNull => CriteriaMatchResult.Rejected,
            _ => CriteriaMatchResult.Rejected
        };
        return ApplyNegation(result, interpreted.Negate);
    }

    private CriteriaMatchResult MatchKnownInteger(
        int? actual,
        ResidualCollectionPredicate predicate)
    {
        var interpreted = CriteriaMatcher.InterpretOperator(predicate.Operator, predicate.Negate);
        if (actual is null)
        {
            var nullResult = interpreted.Operator switch
            {
                CriteriaValueOperator.IsNull => CriteriaMatchResult.Matched,
                CriteriaValueOperator.IsNotNull => CriteriaMatchResult.Rejected,
                _ => CriteriaMatchResult.Rejected
            };
            return ApplyNegation(nullResult, interpreted.Negate);
        }

        if (interpreted.Operator == CriteriaValueOperator.IsNull)
        {
            return ApplyNegation(CriteriaMatchResult.Rejected, interpreted.Negate);
        }

        if (interpreted.Operator == CriteriaValueOperator.IsNotNull)
        {
            return ApplyNegation(CriteriaMatchResult.Matched, interpreted.Negate);
        }

        if (predicate.IntegerValues.Count == 0)
        {
            return CriteriaMatchResult.Unverifiable;
        }

        var expected = predicate.IntegerValues[0];
        var result = interpreted.Operator switch
        {
            CriteriaValueOperator.Equals or CriteriaValueOperator.In =>
                Result(predicate.IntegerValues.Contains(actual.Value)),
            CriteriaValueOperator.GreaterThan => Result(actual.Value > expected),
            CriteriaValueOperator.GreaterThanOrEqual => Result(actual.Value >= expected),
            CriteriaValueOperator.LessThan => Result(actual.Value < expected),
            CriteriaValueOperator.LessThanOrEqual => Result(actual.Value <= expected),
            _ => CriteriaMatchResult.Unverifiable
        };
        return ApplyNegation(result, interpreted.Negate);
    }

    private CriteriaMatchResult MatchUri(
        Uri? actual,
        ResidualCollectionPredicate predicate)
    {
        var interpreted = CriteriaMatcher.InterpretOperator(predicate.Operator, predicate.Negate);
        if (actual is null)
        {
            var nullResult = interpreted.Operator switch
            {
                CriteriaValueOperator.IsNull => CriteriaMatchResult.Matched,
                CriteriaValueOperator.IsNotNull => CriteriaMatchResult.Rejected,
                _ => CriteriaMatchResult.Rejected
            };
            return ApplyNegation(nullResult, interpreted.Negate);
        }

        if (interpreted.Operator == CriteriaValueOperator.IsNull)
        {
            return ApplyNegation(CriteriaMatchResult.Rejected, interpreted.Negate);
        }

        if (interpreted.Operator == CriteriaValueOperator.IsNotNull)
        {
            return ApplyNegation(CriteriaMatchResult.Matched, interpreted.Negate);
        }

        if (interpreted.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In)
        {
            var result = CriteriaMatchResult.Rejected;
            foreach (var value in predicate.StringValues)
            {
                if (!Uri.TryCreate(value, UriKind.Absolute, out var expected))
                {
                    result = CombineOr(result, CriteriaMatchResult.Unverifiable);
                }
                else
                {
                    result = CombineOr(result, Result(actual.Equals(expected)));
                }
            }

            return ApplyNegation(result, interpreted.Negate);
        }

        return CriteriaMatcher.Match(
            actual.AbsoluteUri,
            interpreted.Operator,
            predicate.StringValues,
            interpreted.Negate);
    }

    private IReadOnlyList<CollectionItem> CollectionItems(
        ResidualCollectionKind kind,
        MetadataContribution contribution,
        EntityDto entity,
        IReadOnlyDictionary<string, EntityDto> support,
        string language) => kind switch
        {
            ResidualCollectionKind.ContentRating => ContentRatingCollectionItems(
                entity,
                support,
                language),
            ResidualCollectionKind.Award => AwardCollectionItems(entity, support, language),
            ResidualCollectionKind.Image =>
                [.. contribution.Images.Select(value => new CollectionItem(value, EntityQid: null))],
            ResidualCollectionKind.Reference =>
                [.. contribution.References.Select(value => new CollectionItem(value, EntityQid: null))],
            ResidualCollectionKind.Preview =>
                [.. contribution.Previews.Select(value => new CollectionItem(value, EntityQid: null))],
            ResidualCollectionKind.AlternateText =>
                [
                    .. entity.ToAlternateTexts(
                        language,
                        includeAllAvailableLanguages: true)
                        .Select(value => new CollectionItem(value, EntityQid: null))
                ],
            _ => []
        };

    private IReadOnlyList<CollectionItem> ContentRatingCollectionItems(
        EntityDto entity,
        IReadOnlyDictionary<string, EntityDto> support,
        string language)
    {
        var result = new List<CollectionItem>();
        var candidates = entity.ToContentRatingCandidates();
        var mapped = entity.ToContentRatings(support, language);
        foreach (var ratingSystem in Constants.RatingSystems)
        {
            var mappedRating = mapped.FirstOrDefault(value => string.Equals(
                value.Value.Body,
                ratingSystem.Body,
                StringComparison.OrdinalIgnoreCase));
            var hasPotentialPredecessor = false;
            foreach (var candidate in candidates.Where(value => string.Equals(
                value.PropertyId,
                ratingSystem.PropertyId,
                StringComparison.Ordinal)))
            {
                if (mappedRating is not null &&
                    string.Equals(
                        candidate.EntityQid,
                        mappedRating.EntityQid,
                        StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new CollectionItem(
                        mappedRating.Value,
                        mappedRating.EntityQid,
                        NameComplete: !hasPotentialPredecessor,
                        IdentityComplete: !hasPotentialPredecessor));
                    break;
                }

                if (!support.TryGetValue(candidate.EntityQid, out var rating) || rating.Missing)
                {
                    result.Add(new CollectionItem(
                        new ContentRating
                        {
                            Body = candidate.Body,
                            Rating = string.Empty,
                            Ordinal = result.Count
                        },
                        candidate.EntityQid,
                        Existence: CriteriaMatchResult.Unverifiable,
                        NameComplete: false));
                    hasPotentialPredecessor = true;
                }
            }
        }

        return result;
    }

    private IReadOnlyList<CollectionItem> AwardCollectionItems(
        EntityDto entity,
        IReadOnlyDictionary<string, EntityDto> support,
        string language)
    {
        var result = new List<CollectionItem>();
        var mappedByOrdinal = entity.ToAwards(support, language)
            .Where(value => value.SourceStatementOrdinal is not null)
            .ToDictionary(value => value.SourceStatementOrdinal!.Value);
        var missingYears = new HashSet<int?>();
        foreach (var candidate in entity.ToAwardCandidates())
        {
            var hasPotentialDedupPredecessor = missingYears.Contains(candidate.Year);
            if (mappedByOrdinal.TryGetValue(candidate.SourceStatementOrdinal, out var mapped) &&
                support.TryGetValue(mapped.EntityQid, out var award) &&
                !award.Missing)
            {
                result.Add(new CollectionItem(
                    mapped.Value,
                    mapped.EntityQid,
                    IdentityComplete: !hasPotentialDedupPredecessor,
                    BodyComplete: !hasPotentialDedupPredecessor &&
                        IsAwardBodyComplete(candidate.Statement, award, support, language)));
            }
            else if (!support.TryGetValue(candidate.EntityQid, out var missingAward) || missingAward.Missing)
            {
                result.Add(new CollectionItem(
                    new Award
                    {
                        Body = string.Empty,
                        Name = string.Empty,
                        Status = Constants.WonAwardStatus,
                        Year = candidate.Year,
                        Ordinal = result.Count
                    },
                    candidate.EntityQid,
                    Existence: CriteriaMatchResult.Unverifiable,
                    NameComplete: false,
                    BodyComplete: false));
                missingYears.Add(candidate.Year);
            }
        }

        return result;
    }

    private bool IsAwardBodyComplete(
        StatementDto statement,
        EntityDto award,
        IReadOnlyDictionary<string, EntityDto> support,
        string language)
    {
        var incomplete = false;
        var bodyQids = AwardQualifierBodyQids(statement)
            .Concat(award.EntityClaimQids(Constants.ConferredByPropertyId));
        foreach (var bodyQid in bodyQids)
        {
            if (!support.TryGetValue(bodyQid, out var body) || body.Missing)
            {
                incomplete = true;
                continue;
            }

            if (body.Labels.Resolve(language) is not null)
            {
                return !incomplete;
            }
        }

        return !incomplete;
    }

    private bool UsesAwardBodyValue(ResidualCollectionAlternative alternative) =>
        alternative.Predicates.Any(predicate => predicate.Field == ResidualCollectionField.AwardBody &&
            predicate.Operator is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) &&
            predicate.StringValues.Count > 0);

    private CriteriaMatchResult EvaluateRelationshipSubject(
        ResidualRelationshipFacet facet,
        PendingRelationship relationship,
        SubjectView? subject,
        bool supportHydrated)
    {
        if (facet.Criterion is null)
        {
            return CriteriaMatchResult.Unverifiable;
        }

        var positive = facet.Qids.Contains(relationship.SubjectQid, StringComparer.OrdinalIgnoreCase)
            ? CriteriaMatchResult.Matched
            : CriteriaMatchResult.Rejected;
        if (positive != CriteriaMatchResult.Matched && facet.Names.Count > 0)
        {
            positive = !supportHydrated
                ? CriteriaMatchResult.Unverifiable
                : subject?.Names is null
                    ? CriteriaMatchResult.Unverifiable
                    : MatchAny(subject.Names, facet.Criterion.Operator, facet.Names);
        }

        return ApplyNegation(positive, facet.Criterion.Negate);
    }

    private CriteriaMatchResult EvaluateRelationshipQualifier(
        ResidualRelationshipFacet facet,
        PendingRelationship relationship,
        IReadOnlyDictionary<string, EntityDto> support,
        string language,
        bool supportHydrated)
    {
        if (facet.Criterion is null)
        {
            return CriteriaMatchResult.Unverifiable;
        }

        if (relationship.Qualifier is not null)
        {
            return CriteriaMatcher.Match(
                relationship.Qualifier,
                facet.Criterion.Operator,
                facet.Criterion.Values,
                facet.Criterion.Negate);
        }

        if (relationship.CharacterQid is null)
        {
            return CriteriaMatcher.Match(
                actual: null,
                facet.Criterion.Operator,
                facet.Criterion.Values,
                facet.Criterion.Negate);
        }

        if (facet.Criterion.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return CriteriaMatcher.Match(
                relationship.CharacterQid,
                facet.Criterion.Operator,
                facet.Criterion.Values,
                facet.Criterion.Negate);
        }

        if (facet.Criterion.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In &&
            CriteriaMatcher.Match(
                relationship.CharacterQid,
                facet.Criterion.Operator,
                facet.Criterion.Values) == CriteriaMatchResult.Matched)
        {
            return ApplyNegation(CriteriaMatchResult.Matched, facet.Criterion.Negate);
        }

        if (!supportHydrated ||
            !support.TryGetValue(relationship.CharacterQid, out var character) ||
            character.Missing)
        {
            return CriteriaMatchResult.Unverifiable;
        }

        return CriteriaMatcher.Match(
            character.ResolveLabel(language),
            facet.Criterion.Operator,
            facet.Criterion.Values,
            facet.Criterion.Negate);
    }

    private SubjectView MapSubject(EntityDto entity, string language)
    {
        var resolvedNames = entity.Names(language);
        var names = resolvedNames.Count == 0 ? null : resolvedNames;
        var externalIds = entity.Claims is null
            ? []
            : entity.Claims.Keys.SelectMany(propertyId => entity.StringClaimValues(propertyId)
                .Select(value => new PropertyValue { PropertyId = propertyId, Value = value }))
                .ToArray();
        return new SubjectView(names, externalIds);
    }

    private IEnumerable<string> RequiredSupportQids(
        EntityDto entity,
        IReadOnlyList<ResidualFilter> filters)
    {
        foreach (var filter in filters)
        {
            if (filter.Kind == ResidualFilterKind.Language)
            {
                foreach (var qid in entity.EntityClaimQids(Constants.OriginalLanguagePropertyId))
                {
                    yield return qid;
                }
            }
            else if (filter is
            {
                Kind: ResidualFilterKind.Text,
                TextField: ResidualTextField.OriginalTitle
            })
            {
                foreach (var qid in entity.EntityClaimQids(Constants.OriginalLanguagePropertyId))
                {
                    yield return qid;
                }
            }
            else if (filter is { Kind: ResidualFilterKind.Relationship, RelationshipGroup: not null })
            {
                var alternatives = filter.RelationshipGroup.Alternatives;
                var properties = alternatives.SelectMany(alternative => alternative.PropertyIds)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var relationship in entity.ToEvaluationRelationships()
                    .Where(value => properties.Contains(value.PropertyId)))
                {
                    var matchingAlternatives = alternatives.Where(alternative => alternative.PropertyIds.Contains(
                        relationship.PropertyId,
                        StringComparer.Ordinal)).ToArray();
                    if (matchingAlternatives.Any(alternative => alternative.Subjects.Any(
                        subject => subject.Names.Count > 0) ||
                        alternative.ExternalIdGroups.Any(group =>
                            group.ExternalIds.Count > 0)))
                    {
                        yield return relationship.SubjectQid;
                    }

                    if (relationship.CharacterQid is not null &&
                        matchingAlternatives.Any(alternative => alternative.Qualifiers.Any(qualifier =>
                            qualifier.Criterion?.Operator is not (
                                CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull))))
                    {
                        yield return relationship.CharacterQid;
                    }
                }
            }
            else if (filter is { Kind: ResidualFilterKind.Collection, CollectionGroup: not null })
            {
                foreach (var qid in RequiredCollectionSupportQids(
                    entity,
                    filter.CollectionGroup,
                    includeAwardBodies: false))
                {
                    yield return qid;
                }
            }
        }
    }

    private IEnumerable<string> RequiredDeferredSupportQids(
        EntityDto entity,
        IReadOnlyList<ResidualFilter> filters)
    {
        foreach (var group in filters.Where(filter => filter is
        {
            Kind: ResidualFilterKind.Collection,
            CollectionGroup.Kind: ResidualCollectionKind.Award
        }).Select(filter => filter.CollectionGroup!))
        {
            if (!group.Alternatives.Any(UsesAwardBodyValue))
            {
                continue;
            }

            foreach (var qid in RequiredAwardQualifierBodyQids(entity))
            {
                yield return qid;
            }
        }
    }

    private IEnumerable<string> RequiredCollectionSupportQids(
        EntityDto entity,
        ResidualCollectionGroup group,
        bool includeAwardBodies = true)
    {
        if (group.Kind == ResidualCollectionKind.ContentRating)
        {
            foreach (var candidate in entity.ToContentRatingCandidates().Where(candidate =>
                RatingSystemMayMatchBody(group, candidate.Body)))
            {
                yield return candidate.EntityQid;
            }
        }
        else if (group.Kind == ResidualCollectionKind.Award)
        {
            foreach (var qid in RequiredAwardQids(entity))
            {
                yield return qid;
            }

            if (includeAwardBodies && group.Alternatives.Any(UsesAwardBodyValue))
            {
                foreach (var qid in RequiredAwardQualifierBodyQids(entity))
                {
                    yield return qid;
                }
            }
        }
    }

    private bool RatingSystemMayMatchBody(ResidualCollectionGroup group, string body)
    {
        foreach (var alternative in group.Alternatives)
        {
            var result = CriteriaMatchResult.Matched;
            foreach (var predicate in alternative.Predicates.Where(predicate =>
                predicate.Field == ResidualCollectionField.ContentRatingBody))
            {
                result = CombineAnd(
                    result,
                    CriteriaMatcher.Match(body, predicate.Operator, predicate.StringValues, predicate.Negate));
            }

            if (result != CriteriaMatchResult.Rejected)
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<string> RequiredAwardQids(EntityDto entity) =>
        entity.ToAwardCandidates().Select(candidate => candidate.EntityQid);

    private IEnumerable<string> RequiredAwardQualifierBodyQids(EntityDto entity)
    {
        foreach (var candidate in entity.ToAwardCandidates())
        {
            foreach (var qid in AwardQualifierBodyQids(candidate.Statement))
            {
                yield return qid;
            }
        }
    }

    private IReadOnlyList<string> AwardQualifierBodyQids(StatementDto statement)
    {
        if (statement.Qualifiers is null ||
            !statement.Qualifiers.TryGetValue(Constants.ConferredByPropertyId, out var qualifiers))
        {
            return [];
        }

        return [.. from qualifier in qualifiers
                let qid = qualifier.EntityId()
                where qid is not null
                select qid!];
    }

    private CriteriaMatchResult MatchAny(
        IReadOnlyList<string> actualValues,
        CriteriaValueOperator valueOperator,
        IReadOnlyList<string> expectedValues)
    {
        var result = CriteriaMatchResult.Rejected;
        foreach (var actual in actualValues)
        {
            result = CombineOr(result, CriteriaMatcher.Match(actual, valueOperator, expectedValues));
            if (result == CriteriaMatchResult.Matched)
            {
                break;
            }
        }

        return result;
    }
}
