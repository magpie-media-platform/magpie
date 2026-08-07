using System;
using System.Collections.Generic;
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
    IQueryCache cache,
    ILogger<QueryExecutor> logger) : IQueryExecutor
{
    private const int CandidateLimit = SparqlQueryBuilder.CandidateWindow;

    private const int MaxRequestedLanguages = 50;

    private static readonly IReadOnlyDictionary<string, EntityDto> EmptyEntities =
        new Dictionary<string, EntityDto>();

    private HierarchyRow? MapHierarchyRow(
        IReadOnlyDictionary<string, SparqlBindingDto> binding,
        EntityKind kind)
    {
        var qid = ExtractQid(Read(binding, "child"));
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
            Warnings = warnings,
            CriterionDispositions = plan.CriterionDispositions
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

    private string? ExtractQid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var slash = value!.LastIndexOf('/');
        var qid = slash >= 0 && slash < value.Length - 1 ? value.Substring(slash + 1) : value;
        return qid.Length > 1 && qid[0] == 'Q' && qid.Substring(1).All(char.IsDigit) ? qid : null;
    }

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
                    .Concat(alternative.Names.SelectMany(name => resolutions[name]))
                    .Distinct(StringComparer.Ordinal)
            ],
            Names = []
        };
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

    private void RecordCandidateDeduplication(
        QueryExecution execution,
        ICollection<SearchHit> destination,
        ISet<string> qids,
        IReadOnlyList<SearchHit> hits)
    {
        var before = destination.Count;
        var duplicates = AddDistinct(destination, qids, hits);
        using var invocation = execution.Start(QueryDiagnostics.CandidateDeduplication, "local");
        invocation.RecordRows(hits.Count, destination.Count - before);
        invocation.RecordDuplicatesDropped(duplicates);
    }

    private bool HasCandidateProjection(SearchHit hit) => hit.CandidateProjectionApplied;

    private void SkipCandidatePipeline(
        QueryPlan plan,
        QueryExecution execution,
        string reason)
    {
        if (plan.ProjectCandidateDetails)
        {
            execution.Skip(
                QueryDiagnostics.CandidateProjection,
                "WDQS",
                reason,
                avoidedLogicalInvocations: 1);
        }

        if (plan.ResidualFilters.Count > 0)
        {
            execution.Skip(QueryDiagnostics.ResidualHitEvaluation, "local", reason);
        }

        if (plan.ResidualFilters.Any(value => value.Hydration.HasFlag(ResidualHydration.Entity)))
        {
            execution.Skip(
                QueryDiagnostics.ResidualEntityHydration,
                "Wikidata Action API",
                reason,
                avoidedLogicalInvocations: 1);
        }

        if (plan.ResidualFilters.Any(value => value.Hydration.HasFlag(ResidualHydration.Support)))
        {
            execution.Skip(
                QueryDiagnostics.ResidualSupportHydration,
                "Wikidata Action API",
                reason,
                avoidedLogicalInvocations: 1);
        }
    }

    private void PruneAfterTerminalFailure(
        QueryPlan plan,
        QueryExecution execution,
        string reason)
    {
        var relationshipSeedPlanned = plan.RelationshipSeedFilterIndex() >= 0;
        if (relationshipSeedPlanned && !execution.HasRun(QueryDiagnostics.RelationshipSeedWindows))
        {
            execution.Skip(
                QueryDiagnostics.RelationshipSeedWindows,
                "WDQS",
                reason,
                avoidedLogicalInvocations: 1);
        }

        if (!execution.HasRun(QueryDiagnostics.WdqsWindows))
        {
            execution.Skip(
                QueryDiagnostics.WdqsWindows,
                "WDQS",
                reason,
                avoidedLogicalInvocations: 1);
        }

        if (!execution.HasRun(QueryDiagnostics.CandidateDeduplication))
        {
            execution.Skip(QueryDiagnostics.CandidateDeduplication, "local", reason);
        }

        if (plan.ProjectCandidateDetails &&
            !execution.HasRun(QueryDiagnostics.CandidateProjection))
        {
            execution.Skip(
                QueryDiagnostics.CandidateProjection,
                "WDQS",
                reason,
                avoidedLogicalInvocations: 1);
        }

        if (plan.ResidualFilters.Count > 0 &&
            !execution.HasRun(QueryDiagnostics.ResidualHitEvaluation))
        {
            execution.Skip(QueryDiagnostics.ResidualHitEvaluation, "local", reason);
        }

        if (plan.ResidualFilters.Any(value => value.Hydration.HasFlag(ResidualHydration.Entity)) &&
            !execution.HasRun(QueryDiagnostics.ResidualEntityHydration))
        {
            execution.Skip(
                QueryDiagnostics.ResidualEntityHydration,
                "Wikidata Action API",
                reason,
                avoidedLogicalInvocations: 1);
        }

        if (plan.ResidualFilters.Any(value => value.Hydration.HasFlag(ResidualHydration.Support)) &&
            !execution.HasRun(QueryDiagnostics.ResidualSupportHydration))
        {
            execution.Skip(
                QueryDiagnostics.ResidualSupportHydration,
                "Wikidata Action API",
                reason,
                avoidedLogicalInvocations: 1);
        }
    }

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
        IReadOnlyDictionary<string, HashSet<string>> unverifiableByQid)
    {
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
        var traceEnabled = logger.IsEnabled(LogLevel.Trace);
        var execution = new QueryExecution(plan.Fingerprint);
        if (traceEnabled)
        {
            var planJson = plan.SerializePlan();
            logger.LogTrace(
                "Wikidata PLAN for {Provider} fingerprint {Fingerprint}: {PlanJson}",
                Constants.ProviderType,
                execution.Fingerprint,
                planJson);
        }

        var pageOffset = (plan.Page - 1) * plan.MaximumResults;
        if (plan.Strategy == QueryStrategy.Unsupported)
        {
            var unsupported = new QueryResult
            {
                Supported = false,
                PageInfo = ProviderSearchPageInfo.Unsupported,
                CriterionDispositions = plan.CriterionDispositions
            };
            return FinalizeExecution(
                unsupported,
                execution,
                pageOffset,
                verifiedSurvivorRows: 0,
                "The requested entity kind is unsupported.",
                traceEnabled);
        }

        if (plan.Strategy == QueryStrategy.Empty)
        {
            var empty = Empty(plan, plan.Warnings);
            var unsatisfiableCriteria = plan.CriterionDispositions
                .Where(value => value.Disposition == CriterionDispositionKind.ProvablyUnsatisfiable)
                .Select(value => value.Source + ": " + value.Reason)
                .ToArray();
            var reason = unsatisfiableCriteria.Length > 0
                ? "The translation contains provably unsatisfiable criteria."
                : "The translation produced no bounded WDQS query.";
            return FinalizeExecution(
                empty,
                execution,
                pageOffset,
                verifiedSurvivorRows: 0,
                reason,
                traceEnabled);
        }

        try
        {
            var resolvedPlan = plan;
            resolvedPlan = await ResolveTitleCandidatesAsync(resolvedPlan, context, execution, cancellationToken)
                .ConfigureAwait(false);
            resolvedPlan = await ResolveEntityNamesAsync(resolvedPlan, context, execution, cancellationToken)
                .ConfigureAwait(false);
            plan = resolvedPlan;
            if (plan.Strategy == QueryStrategy.Empty)
            {
                const string reason = "Entity resolution proved every positive selector unsatisfiable.";
                execution.Skip(
                    QueryDiagnostics.WdqsWindows,
                    "WDQS",
                    "Precondition unsatisfied: entity resolution returned no ids.",
                    avoidedLogicalInvocations: 1);
                SkipCandidatePipeline(
                    plan,
                    execution,
                    "Precondition unsatisfied: entity resolution returned no ids.");
                return FinalizeExecution(
                    Empty(plan, plan.Warnings),
                    execution,
                    pageOffset,
                    verifiedSurvivorRows: 0,
                    reason,
                    traceEnabled);
            }

            var logicalOffset = checked((plan.Page - 1) * plan.MaximumResults);
            var logicalBoundary = plan.UsesLocalPaging
                ? checked(logicalOffset + plan.MaximumResults + 1)
                : checked(plan.MaximumResults + 1);
            var collected = new List<SearchHit>();
            var collectedQids = new HashSet<string>(StringComparer.Ordinal);
            var executionWarnings = new List<string>();
            var sourceExhausted = false;
            var relationshipIndex = plan.RelationshipSeedFilterIndex();
            if (relationshipIndex >= 0)
            {
                var seedOffset = 0;
                while (collected.Count < logicalBoundary)
                {
                    var seedRequest = SparqlQueryBuilder.BuildRelationshipSeed(
                        plan.EntityKind,
                        plan.Filters[relationshipIndex],
                        seedOffset);
                    var seedResponse = await ExecuteRequestAsync(
                        seedRequest,
                        context,
                        execution,
                        QueryDiagnostics.RelationshipSeedWindows,
                        traceEnabled,
                        cancellationToken).ConfigureAwait(false);
                    using var seedInvocation = seedResponse.Invocation;
                    var rawSeedQids = (from row in seedResponse.Rows
                                       let qid = ExtractQid(Read(row, "item"))
                                       where qid is not null
                                       select qid!).ToArray();
                    var seedQids = rawSeedQids.Distinct(StringComparer.Ordinal).ToArray();
                    seedResponse.Invocation.RecordRows(seedResponse.Rows.Count, rawSeedQids.Length);
                    seedResponse.Invocation.RecordRejection(
                        "invalidQid",
                        seedResponse.Rows.Count - rawSeedQids.Length);
                    seedResponse.Invocation.RecordIdsProduced(seedQids.Length);
                    seedResponse.Invocation.RecordDuplicatesDropped(rawSeedQids.Length - seedQids.Length);
                    var seedHasMore = seedQids.Length > SparqlQueryBuilder.CandidateWindow;
                    seedResponse.Invocation.RecordLookaheadRows(
                        Math.Max(0, seedQids.Length - SparqlQueryBuilder.CandidateWindow));
                    var windowQids = seedQids.Take(SparqlQueryBuilder.CandidateWindow).ToArray();
                    if (windowQids.Length == 0)
                    {
                        seedResponse.Invocation.RecordReason("The relationship seed source was exhausted.");
                        execution.Skip(
                            QueryDiagnostics.WdqsWindows,
                            "WDQS",
                            "Precondition unsatisfied: the relationship seed window was empty.",
                            avoidedLogicalInvocations: 1);
                        SkipCandidatePipeline(
                            plan,
                            execution,
                            "Precondition unsatisfied: the relationship seed window was empty.");
                        sourceExhausted = true;
                        break;
                    }

                    seedInvocation.Dispose();

                    var seededPlan = BindRelationshipSeed(plan, relationshipIndex, windowQids);
                    var window = await SearchCandidateWindowAsync(
                        seededPlan,
                        context,
                        0,
                        execution,
                        traceEnabled,
                        cancellationToken).ConfigureAwait(false);
                    RecordCandidateDeduplication(execution, collected, collectedQids, window.Hits);
                    executionWarnings.AddRange(window.Warnings);
                    if (!seedHasMore)
                    {
                        sourceExhausted = true;
                        break;
                    }

                    seedOffset += SparqlQueryBuilder.CandidateWindow;
                }
            }
            else
            {
                var candidateOffset = 0;
                while (collected.Count < logicalBoundary)
                {
                    var window = await SearchCandidateWindowAsync(
                        plan,
                        context,
                        candidateOffset,
                        execution,
                        traceEnabled,
                        cancellationToken).ConfigureAwait(false);
                    RecordCandidateDeduplication(execution, collected, collectedQids, window.Hits);
                    executionWarnings.AddRange(window.Warnings);
                    if (!plan.UsesLocalPaging || !window.HasMore)
                    {
                        sourceExhausted = true;
                        break;
                    }

                    candidateOffset += SparqlQueryBuilder.CandidateWindow;
                }
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
                ],
                CriterionDispositions = plan.CriterionDispositions
            };
            return FinalizeExecution(
                result,
                execution,
                logicalOffset,
                collected.Count,
                hasMore
                    ? "The survivor boundary included a lookahead row."
                    : sourceExhausted
                        ? "The upstream source was exhausted."
                        : "The caller result boundary was reached.",
                traceEnabled,
                offsetRowsDropped: plan.UsesLocalPaging ? Math.Min(logicalOffset, collected.Count) : 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PruneAfterTerminalFailure(plan, execution, "The caller cancelled Wikidata execution.");
            _ = FinalizeExecution(
                result: null,
                execution: execution,
                offset: 0,
                verifiedSurvivorRows: 0,
                reason: "The caller cancelled Wikidata execution.",
                traceEnabled: traceEnabled,
                finalPagingRan: false);
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            PruneAfterTerminalFailure(plan, execution, "The Wikidata execution timeout was reached.");
            return FinalizeExecution(
                Failure(plan, "Wikidata search timed out; no Wikidata results were returned."),
                execution,
                pageOffset,
                verifiedSurvivorRows: 0,
                "The Wikidata execution timeout was reached.",
                traceEnabled,
                boundHit: true,
                finalPagingRan: false);
        }
        catch (HttpRequestException exception) when (IsRateLimited(exception))
        {
            logger.LogWarning("Wikidata rate limited the search for entity kind {EntityKind}", plan.EntityKind);
            PruneAfterTerminalFailure(plan, execution, "Wikidata execution was rate limited.");
            return FinalizeExecution(
                Failure(plan, "Wikidata search was rate limited; no Wikidata results were returned."),
                execution,
                pageOffset,
                verifiedSurvivorRows: 0,
                "Wikidata execution was rate limited.",
                traceEnabled,
                finalPagingRan: false);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            logger.LogWarning(exception, "Wikidata search failed for entity kind {EntityKind}", plan.EntityKind);
            PruneAfterTerminalFailure(plan, execution, "Wikidata execution failed.");
            return FinalizeExecution(
                Failure(plan, "Wikidata search failed; no Wikidata results were returned."),
                execution,
                pageOffset,
                verifiedSurvivorRows: 0,
                "Wikidata execution failed.",
                traceEnabled,
                finalPagingRan: false);
        }
        catch (Exception)
        {
            PruneAfterTerminalFailure(
                plan,
                execution,
                "Wikidata execution terminated with an unexpected failure.");
            _ = FinalizeExecution(
                result: null,
                execution: execution,
                offset: 0,
                verifiedSurvivorRows: 0,
                reason: "Wikidata execution terminated with an unexpected failure.",
                traceEnabled: traceEnabled,
                finalPagingRan: false);
            throw;
        }
    }

    private async Task<CandidateWindowResult> SearchCandidateWindowAsync(
        QueryPlan plan,
        RequestContext context,
        int candidateOffset,
        QueryExecution execution,
        bool traceEnabled,
        CancellationToken cancellationToken)
    {
        var response = await ExecuteRequestAsync(
            SparqlQueryBuilder.BuildSearch(plan, context.Language, candidateOffset),
            context,
            execution,
            QueryDiagnostics.WdqsWindows,
            traceEnabled,
            cancellationToken).ConfigureAwait(false);
        using var invocation = response.Invocation;
        execution.AddFetchedRows(response.Rows.Count);
        var sentinelsRemoved = plan.Strategy == QueryStrategy.TitleSearch
            ? response.Rows.Count(IsTitleWindowSentinel)
            : 0;
        var lookaheadRows = plan.Strategy == QueryStrategy.TitleSearch
            ? sentinelsRemoved
            : plan.UsesLocalPaging
                ? Math.Max(0, response.Rows.Count - SparqlQueryBuilder.CandidateWindow)
                : Math.Max(0, response.Rows.Count - plan.MaximumResults);
        var sourceHasMore = lookaheadRows > 0;
        var visibleRows = plan.Strategy switch
        {
            QueryStrategy.TitleSearch => response.Rows.Where(row => !IsTitleWindowSentinel(row)),
            _ when plan.UsesLocalPaging => response.Rows.Take(SparqlQueryBuilder.CandidateWindow),
            _ => response.Rows
        };
        var hits = (from row in visibleRows
                    let hit = row.ToSearchHit()
                    where hit is not null
                    select hit!).ToArray();
        var filtered = FilterCandidates(plan, hits);
        var visibleRowCount = plan.Strategy == QueryStrategy.TitleSearch
            ? response.Rows.Count - sentinelsRemoved
            : plan.UsesLocalPaging
                ? Math.Min(response.Rows.Count, SparqlQueryBuilder.CandidateWindow)
                : response.Rows.Count;
        invocation.RecordSentinelsDropped(sentinelsRemoved);
        invocation.RecordLookaheadRows(lookaheadRows);
        invocation.RecordRows(visibleRowCount, filtered.Count);
        invocation.RecordRejection("candidateProjection", visibleRowCount - hits.Length);
        invocation.RecordRejection("candidateFilter", hits.Length - filtered.Count);
        invocation.RecordReason(
            sourceHasMore
                ? "The source returned a lookahead row."
                : "The source window did not include a lookahead row.");
        invocation.Dispose();
        if (filtered.Count == 0)
        {
            SkipCandidatePipeline(plan, execution, "Input empty after candidate mapping and filtering.");
        }

        var verified = new List<SearchHit>();
        var warnings = new List<string>();
        for (var offset = 0; offset < filtered.Count; offset += SparqlQueryBuilder.CandidateWindow)
        {
            var batch = filtered.Skip(offset).Take(SparqlQueryBuilder.CandidateWindow).ToArray();
            var projection = await ProjectCandidatesAsync(
                plan,
                batch,
                context,
                execution,
                traceEnabled,
                cancellationToken)
                .ConfigureAwait(false);
            warnings.AddRange(projection.Warnings);
            if (plan.ResidualFilters.Count == 0 || projection.Hits.Count == 0)
            {
                verified.AddRange(projection.Hits);
                continue;
            }

            var evaluation = await ApplyResidualsAsync(
                plan,
                projection.Hits,
                context,
                cancellationToken,
                execution).ConfigureAwait(false);
            verified.AddRange(evaluation.Hits);
            warnings.AddRange(evaluation.Warnings);
        }

        return new CandidateWindowResult(verified, warnings, sourceHasMore);
    }

    private async Task<SearchResponse> ExecuteRequestAsync(
        SparqlRequest request,
        RequestContext context,
        QueryExecution execution,
        string stage,
        bool traceEnabled,
        CancellationToken cancellationToken)
    {
        var invocation = execution.Start(stage, "WDQS");
        invocation.RecordWindow();
        SearchCacheResult cached;
        try
        {
            cached = await cache.GetSearchAsync(
                request.CanonicalCacheKey,
                async token =>
                {
                    try
                    {
                        return await client.ExecuteSparqlAsync(request, context, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        invocation.RecordSparqlRequest(request.QueryText, traceEnabled);
                    }
                },
                cancellationToken,
                invocation.RecordCacheProvenance).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            or OperationCanceledException)
        {
            invocation.RecordReason("The request did not complete successfully: " + exception.GetType().Name);
            invocation.Dispose();
            throw;
        }

        invocation.RecordRowsProduced(cached.Rows.Count);
        return new SearchResponse(cached.Rows, invocation);
    }

    private async Task<QueryPlan> ResolveTitleCandidatesAsync(
        QueryPlan plan,
        RequestContext context,
        QueryExecution execution,
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
            using var invocation = execution.Start(
                QueryDiagnostics.TitleResolution,
                "Wikidata Action API");
            try
            {
                var key = language.Trim().ToLowerInvariant() + "|title|" + term.Trim().ToLowerInvariant();
                var resolved = await cache.GetEntitySearchAsync(
                    key,
                    async token =>
                    {
                        try
                        {
                            return await client.SearchEntitiesAsync(term, context, token).ConfigureAwait(false);
                        }
                        finally
                        {
                            invocation.RecordHttpRequest();
                        }
                    },
                    cancellationToken,
                    invocation).ConfigureAwait(false);
                candidates.AddRange(resolved);
                invocation.RecordRows(1, resolved.Count > 0 ? 1 : 0);
                invocation.RecordIdsProduced(resolved.Count);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                invocation.RecordReason("A title candidate lookup timed out and contributed no candidates.");
                logger.LogDebug("Wikidata title candidate lookup timed out for {Term}", term);
            }
            catch (HttpRequestException exception)
            {
                invocation.RecordReason("A title candidate lookup failed and contributed no candidates.");
                logger.LogDebug(exception, "Wikidata title candidate lookup failed for {Term}", term);
            }
            catch (JsonException exception)
            {
                invocation.RecordReason(
                    "A title candidate lookup returned invalid data and contributed no candidates.");
                logger.LogDebug(exception, "Wikidata title candidate lookup returned invalid data for {Term}", term);
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
        QueryExecution execution,
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
            using var invocation = execution.Start(
                QueryDiagnostics.EntityResolution,
                "Wikidata Action API");
            var resolved = await cache.GetEntitySearchAsync(
                key,
                async token =>
                {
                    try
                    {
                        return await client.SearchEntitiesAsync(name, context, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        invocation.RecordHttpRequest();
                    }
                },
                cancellationToken,
                invocation).ConfigureAwait(false);
            resolutions[name] = resolved;
            invocation.RecordRows(1, resolved.Count > 0 ? 1 : 0);
            invocation.RecordIdsProduced(resolved.Count);
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

                execution.RecordReason(
                    QueryDiagnostics.EntityResolution,
                    "Wikidata Action API",
                    "A positive named selector resolved to no ids, proving the result empty.");
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
            QueryExecution execution,
            bool traceEnabled,
            CancellationToken cancellationToken)
    {
        var alreadyProjected = hits.Count > 0 && hits.All(HasCandidateProjection);
        if (hits.Count == 0 ||
            !plan.ProjectCandidateDetails ||
            alreadyProjected)
        {
            if (plan.ProjectCandidateDetails)
            {
                execution.Skip(
                    QueryDiagnostics.CandidateProjection,
                    "WDQS",
                    hits.Count == 0
                        ? "Input empty after candidate mapping and filtering."
                        : "Every candidate already carried the projected fields.",
                    avoidedLogicalInvocations: 1);
            }

            return (hits, []);
        }

        var request = SparqlQueryBuilder.BuildCandidateProjection(
            plan.EntityKind,
            [.. hits.Select(hit => hit.Qid)],
            context.Language);
        SearchResponse response;
        try
        {
            response = await ExecuteRequestAsync(
                request,
                context,
                execution,
                QueryDiagnostics.CandidateProjection,
                traceEnabled,
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
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
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

        using var invocation = response.Invocation;
        var rows = response.Rows;

        var projections = (from row in rows
                           let qid = ExtractQid(Read(row, "item"))
                           where qid is not null
                           group row by qid! into matches
                           select matches)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        invocation.RecordRows(hits.Count, hits.Count);
        if (projections.Count < hits.Count)
        {
            invocation.RecordReason("Some candidates had no matching projection row and were retained unchanged.");
        }

        return (
            [.. hits.Select(hit => projections.TryGetValue(hit.Qid, out var row)
                ? hit.ApplyProjection(row)
                : hit)],
            []);
    }

    private QueryResult FinalizeExecution(
        QueryResult? result,
        QueryExecution execution,
        int offset,
        int verifiedSurvivorRows,
        string reason,
        bool traceEnabled,
        int offsetRowsDropped = 0,
        bool boundHit = false,
        bool finalPagingRan = true)
    {
        if (finalPagingRan)
        {
            using var invocation = execution.Start(QueryDiagnostics.FinalPaging, "local");
            invocation.RecordRows(verifiedSurvivorRows, result?.Hits.Count ?? 0);
            invocation.RecordOffsetRowsDropped(offsetRowsDropped);
            invocation.RecordReason(
                "Requested offset " +
                offset.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ". " +
                reason);
        }
        else
        {
            execution.Skip(QueryDiagnostics.FinalPaging, "local", reason);
        }

        execution.Freeze(
            result?.Hits.Count ?? 0,
            result?.PageInfo.HasMore ?? false,
            boundHit,
            reason,
            result?.Warnings.Count ?? 0);
        var sparqlTraceRecords = execution.DrainSparqlTraceRecords();
        if (traceEnabled)
        {
            foreach (var record in sparqlTraceRecords)
            {
                logger.LogTrace(
                    "Wikidata SPARQL for fingerprint {Fingerprint} stage {Stage} invocation " +
                    "{Invocation} provenance {Provenance}:\n{QueryText}",
                    execution.Fingerprint,
                    record.Stage,
                    record.Invocation,
                    SearchCacheProvenance.Remote,
                    record.QueryText);
            }

            var executionJson = execution.SerializeExecution();
            logger.LogTrace(
                "Wikidata EXEC for {Provider} fingerprint {Fingerprint}: {ExecutionJson}",
                Constants.ProviderType,
                execution.Fingerprint,
                executionJson);
        }

        return result is null ? null! : result with { Execution = execution };
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
        QueryExecution.StageInvocation? invocation = null)
    {
        return HydrateAsync(qids, CandidateLimit, context, cancellationToken, invocation);
    }

    private Task<ResidualHydrationBatch> HydrateSupportAsync(
        IEnumerable<string> qids,
        RequestContext context,
        CancellationToken cancellationToken,
        QueryExecution.StageInvocation? invocation = null)
    {
        return HydrateAsync(qids, null, context, cancellationToken, invocation);
    }

    private async Task<ResidualHydrationBatch> HydrateAsync(
        IEnumerable<string> qids,
        int? limit,
        RequestContext context,
        CancellationToken cancellationToken,
        QueryExecution.StageInvocation? invocation)
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
            var entities = await cache.GetEntitiesAsync(requested, context, cancellationToken, invocation)
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
            or OperationCanceledException)
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
        QueryExecution? execution = null)
    {
        var unverifiableByQid = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        using var hitInvocation = execution?.Start(QueryDiagnostics.ResidualHitEvaluation, "local");
        var survivors = ApplyHitResiduals(plan.ResidualFilters, hits, unverifiableByQid, hitInvocation);
        hitInvocation?.RecordRows(hits.Count, survivors.Count);
        hitInvocation?.Dispose();
        if (survivors.Count == 0)
        {
            if (plan.ResidualFilters.Any(value => value.Hydration.HasFlag(ResidualHydration.Entity)))
            {
                execution?.Skip(
                    QueryDiagnostics.ResidualEntityHydration,
                    "Wikidata Action API",
                    "Input empty after hit residual evaluation.",
                    avoidedLogicalInvocations: 1);
            }

            if (plan.ResidualFilters.Any(value => value.Hydration.HasFlag(ResidualHydration.Support)))
            {
                execution?.Skip(
                    QueryDiagnostics.ResidualSupportHydration,
                    "Wikidata Action API",
                    "Input empty after hit residual evaluation.",
                    avoidedLogicalInvocations: 1);
            }

            return Result(survivors, unverifiableByQid);
        }

        var entityResiduals = plan.ResidualFilters
            .Where(filter => filter.Hydration.HasFlag(ResidualHydration.Entity))
            .ToArray();
        var hasSupportResiduals = entityResiduals.Any(filter =>
            filter.Hydration.HasFlag(ResidualHydration.Support));
        if (entityResiduals.Length == 0)
        {
            return Result(survivors, unverifiableByQid);
        }

        var entityContext = EntityHydrationContext(entityResiduals, context);
        using var entityInvocation = execution?.Start(
            QueryDiagnostics.ResidualEntityHydration,
            "Wikidata Action API");
        var entityBatch = await HydrateCandidatesAsync(
            survivors.Select(hit => hit.Qid),
            entityContext,
            cancellationToken,
            entityInvocation).ConfigureAwait(false);
        entityInvocation?.RecordIdsProduced(entityBatch.Entities.Count);
        if (entityBatch.Failed)
        {
            foreach (var hit in survivors)
            {
                foreach (var filter in entityResiduals)
                {
                    AddUnverifiable(unverifiableByQid, hit.Qid, filter.Source);
                }
            }

            entityInvocation?.RecordRows(survivors.Count, survivors.Count);
            entityInvocation?.RecordReason("Hydration failed; candidates were retained as unverifiable.");
            if (hasSupportResiduals)
            {
                execution?.Skip(
                    QueryDiagnostics.ResidualSupportHydration,
                    "Wikidata Action API",
                    "Candidate entity hydration failed.");
            }

            return Result(survivors, unverifiableByQid);
        }

        var states = ApplyEntityResiduals(
            plan,
            survivors,
            entityResiduals,
            entityBatch.Entities,
            context.Language,
            unverifiableByQid,
            entityInvocation);
        entityInvocation?.RecordRows(survivors.Count, states.Count);
        entityInvocation?.Dispose();
        if (states.Count == 0)
        {
            if (hasSupportResiduals)
            {
                execution?.Skip(
                    QueryDiagnostics.ResidualSupportHydration,
                    "Wikidata Action API",
                    "Input empty after candidate entity residual evaluation.",
                    avoidedLogicalInvocations: 1);
            }

            return Result([], unverifiableByQid);
        }

        if (!hasSupportResiduals)
        {
            return Result([.. states.Select(state => state.Hit)], unverifiableByQid);
        }

        var supportStates = states.Where(state => state.Pending.Count > 0).ToArray();
        if (supportStates.Length == 0)
        {
            execution?.Skip(
                QueryDiagnostics.ResidualSupportHydration,
                "Wikidata Action API",
                "All residual predicates were settled from candidate entities.",
                avoidedLogicalInvocations: 1);
            return Result([.. states.Select(state => state.Hit)], unverifiableByQid);
        }

        var supportQids = supportStates
            .SelectMany(state => RequiredSupportQids(state.Entity!, state.Pending))
            .Concat(supportStates.SelectMany(state => RequiredDeferredSupportQids(state.Entity!, state.Pending)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ResidualHydrationBatch supportBatch;
        QueryExecution.StageInvocation? supportInvocation = null;
        if (supportQids.Length == 0)
        {
            execution?.Skip(
                QueryDiagnostics.ResidualSupportHydration,
                "Wikidata Action API",
                "No support entity ids were required after candidate entity evaluation.",
                avoidedLogicalInvocations: 1);
            supportBatch = new ResidualHydrationBatch
            {
                Entities = EmptyEntities,
                RequestedCount = 0
            };
        }
        else
        {
            supportInvocation = execution?.Start(
                QueryDiagnostics.ResidualSupportHydration,
                "Wikidata Action API");
            supportBatch = await HydrateSupportAsync(
                supportQids,
                context,
                cancellationToken,
                supportInvocation).ConfigureAwait(false);
            supportInvocation?.RecordIdsProduced(supportBatch.Entities.Count);
            if (supportBatch.Failed)
            {
                supportInvocation?.RecordReason("Hydration failed; candidates were retained as unverifiable.");
            }
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

            supportInvocation?.RecordRows(states.Count, states.Count);
            supportInvocation?.Dispose();
            return Result([.. states.Select(state => state.Hit)], unverifiableByQid);
        }

        var finalStates = ApplySupportResiduals(
            plan,
            states,
            supportBatch.Entities,
            context.Language,
            unverifiableByQid,
            supportInvocation);
        supportInvocation?.RecordRows(states.Count, finalStates.Count);
        supportInvocation?.Dispose();
        return Result([.. finalStates.Select(state => state.Hit)], unverifiableByQid);
    }

    private IReadOnlyList<SearchHit> ApplyHitResiduals(
        IReadOnlyList<ResidualFilter> filters,
        IReadOnlyList<SearchHit> hits,
        IDictionary<string, HashSet<string>> unverifiableByQid,
        QueryExecution.StageInvocation? invocation)
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
            invocation?.RecordRejection(filter.Source, before - survivors.Count);
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
        QueryExecution.StageInvocation? invocation)
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
            invocation?.RecordRejection(filter.Source, before - states.Count);
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
        QueryExecution.StageInvocation? invocation)
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
            invocation?.RecordRejection(filter.Source, before - result.Count);
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
                let qid = EntityId(qualifier)
                where qid is not null
                select qid!];
    }

    private string? EntityId(SnakDto? snak)
    {
        var value = snak is { SnakType: null or "value" } ? snak.DataValue?.Value : null;
        if (value is not { ValueKind: JsonValueKind.Object } element ||
            !element.TryGetProperty("id", out var id) ||
            id.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(id.GetString()))
        {
            return null;
        }

        var qid = id.GetString()!.Trim();
        if (qid.Length <= 1 ||
            qid[0] is not ('Q' or 'q') ||
            qid[1] == '0' ||
            qid.Skip(1).Any(value => value is < '0' or > '9'))
        {
            return null;
        }

        return qid[0] == 'q' ? "Q" + qid.Substring(1) : qid;
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

    private sealed record CandidateWindowResult(
        IReadOnlyList<SearchHit> Hits,
        IReadOnlyList<string> Warnings,
        bool HasMore);

    private sealed record SearchResponse(
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> Rows,
        QueryExecution.StageInvocation Invocation);

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
}
