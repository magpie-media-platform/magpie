using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed class QueryExecutor(
    ITmdbClient client,
    IQueryCoordinator cache,
    ILogger<QueryExecutor> logger) : IQueryExecutor
{
    private static readonly SubjectResolutionSet EmptySubjectSet = new()
    {
        Groups = [],
        Warnings = [],
        RequiredGroupUnresolved = false,
        HadRemoteFailure = false
    };

    private sealed record PageResult(IReadOnlyList<SearchHit> Hits, int TotalPages, int TotalResults);

    private sealed class ExecutionState
    {
        internal HashSet<int> ResolvedNodeIds { get; } = [];

        internal SubjectResolutionSet SubjectSet { get; set; } = EmptySubjectSet;

        internal IReadOnlyDictionary<string, int> GenreIds { get; set; } =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        internal string? ImageBase { get; set; }

        internal bool ProvablyEmpty { get; set; }

        internal bool ResultLimitSatisfied { get; set; }
    }

    [SuppressMessage("Ordering", "SA1201", Justification = "Nested types come first in this codebase.")]
    private const int MaximumConcurrency = 4;

    private const int SourcePageSize = 20;

    private const int MaximumResolvedIdsPerDiscoverRequest = 200;

    public async Task<QueryExecution> ExecuteAsync(
        QueryPlan plan,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>(plan.Warnings);
        try
        {
            if (plan.Root is UnionPlanNode { Children.Count: 0 })
            {
                return LogicalResult([], warnings, plan.RequestedPage);
            }

            var state = new ExecutionState();
            if (context.Progress is { } progress)
            {
                await EnsurePrerequisitesAsync(plan.Root, plan, context, state, warnings, cancellationToken)
                    .ConfigureAwait(false);
                if (state.SubjectSet.HadRemoteFailure)
                {
                    warnings.Add("TMDb could not resolve every search source. More results may remain.");
                    return LogicalResult([], warnings, plan.RequestedPage);
                }

                var resolved = JsonSerializer.Serialize(new
                {
                    Groups = state.SubjectSet.Groups.Select(group => new
                    {
                        group.BindingKey,
                        group.Excluded,
                        group.Subjects,
                        group.Incomplete,
                        group.IncompleteRoles
                    }),
                    Genres = state.GenreIds.OrderBy(pair => pair.Key)
                });
                if (progress.ResolvedSources is not null && progress.ResolvedSources != resolved)
                {
                    throw new MetadataSearchContinuationException("TMDb resolved sources changed.");
                }

                progress.ResolvedSources = resolved;
                ValidateContinuationSources(plan.Root, context, state);
            }

            if (context.ValidateOnly)
            {
                return LogicalResult([], warnings, plan.RequestedPage);
            }

            return await ExecuteNodeAsync(
                plan.Root,
                plan,
                context,
                state,
                warnings,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MetadataSearchContinuationException)
        {
            throw;
        }
        catch (Magpie.Models.UpstreamThrottledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TMDb query execution failed for plan root {PlanRoot}", plan.Root.GetType().Name);
            warnings.Add("TMDb could not complete the remote query.");
            var failure = LogicalResult([], warnings, plan.RequestedPage);
            failure.Diagnostics.StreamFaulted = true;
            return failure;
        }
    }

    private IReadOnlyList<SearchHit> MapCredits(CreditResponse response, ResolvedSubject subject)
    {
        var hits = new List<SearchHit>();
        if (subject.Role is SubjectRole.Person or SubjectRole.Cast && response.Tv?.Cast is not null)
        {
            hits.AddRange(response.Tv.Cast.Select((hit, index) => hit.FromTvCredit(subject, cast: true, index)));
        }

        if (subject.Role is SubjectRole.Person or SubjectRole.Crew
            or SubjectRole.Director or SubjectRole.Writer &&
            response.Tv?.Crew is not null)
        {
            hits.AddRange(response.Tv.Crew
                .Where(hit => JobMatches(subject.Role, hit.Job))
                .Select((hit, index) => hit.FromTvCredit(subject, cast: false, index)));
        }

        return hits;
    }

    private bool PositiveGenreCannotMatch(ResolvePlanNode resolve, IReadOnlyDictionary<string, int> genreIds)
    {
        return resolve.GenreFilters.Any(filter => !filter.Excluded && filter.ResolveGenreIds(genreIds).Count == 0);
    }

    private bool HasExternalIdExclusions(IReadOnlyList<ContentIdFilter> filters)
    {
        return filters.Any(criterion => criterion.Excluded &&
            criterion.Alternatives.Any(id =>
                !id.Source.Equals(Constants.ProviderType, StringComparison.OrdinalIgnoreCase)));
    }

    private bool HasPersonSubjects(
        IReadOnlyList<SubjectResolutionBinding> bindings,
        IReadOnlyList<ResolvedSubjectGroup> subjects)
    {
        return subjects.Any(group => group.Subjects.Any(subject =>
            subject.Role is not SubjectRole.Company and not SubjectRole.Network)) ||
        bindings.Any(binding => binding.Filter.Alternatives.Any(alternative =>
            alternative.Roles.Any(role => role is not SubjectRole.Company and not SubjectRole.Network)));
    }

    private bool HasElementFamily(IReadOnlyList<ElementFilter> filters, ElementFamily family)
    {
        return filters.Any(criterion => criterion.Family == family);
    }

    private bool JobMatches(SubjectRole role, string? job)
    {
        if (role is SubjectRole.Person or SubjectRole.Crew)
        {
            return true;
        }

        var classified = job.ClassifyCrewJob();
        return role == SubjectRole.Director
            ? classified == Constants.Director
            : role != SubjectRole.Writer || classified == Constants.Writer;
    }

    private List<SearchHit> MergeHits(IEnumerable<SearchHit> hits)
    {
        var merged = new List<SearchHit>();
        foreach (var hit in hits)
        {
            AddOrMerge(merged, hit);
        }

        return merged;
    }

    private List<SearchHit> IntersectHits(IReadOnlyList<SearchHit> left, IReadOnlyList<SearchHit> right)
    {
        var rightById = right
            .GroupBy(hit => (hit.Route, hit.Id))
            .ToDictionary(group => group.Key, group => group.First());
        return [.. left.GroupBy(hit => (hit.Route, hit.Id))
            .Select(group => group.First())
            .Where(hit => rightById.ContainsKey((hit.Route, hit.Id)))
            .Select(hit => MergeHit(hit, rightById[(hit.Route, hit.Id)]))];
    }

    private void AddOrMerge(List<SearchHit> hits, SearchHit hit)
    {
        var index = hits.FindIndex(existing => existing.Id == hit.Id);
        if (index < 0)
        {
            hits.Add(hit);
            return;
        }

        hits[index] = MergeHit(hits[index], hit);
    }

    private SearchHit MergeHit(SearchHit first, SearchHit second)
    {
        return first with
        {
            Title = first.Title ?? second.Title,
            OriginalTitle = first.OriginalTitle ?? second.OriginalTitle,
            Overview = first.Overview ?? second.Overview,
            OriginalLanguage = first.OriginalLanguage ?? second.OriginalLanguage,
            ReleaseDate = first.ReleaseDate ?? second.ReleaseDate,
            Duration = first.Duration ?? second.Duration,
            ProductionStatus = first.ProductionStatus ?? second.ProductionStatus,
            GenreIds = [.. first.GenreIds.Concat(second.GenreIds).Distinct()],
            GenreNames = [.. first.GenreNames.Concat(second.GenreNames).Distinct(StringComparer.OrdinalIgnoreCase)],
            PosterPath = first.PosterPath ?? second.PosterPath,
            BackdropPath = first.BackdropPath ?? second.BackdropPath,
            Homepage = first.Homepage ?? second.Homepage,
            ImageGallery = first.ImageGallery ?? second.ImageGallery,
            AlternateTexts = first.AlternateTexts.Count > 0 ? first.AlternateTexts : second.AlternateTexts,
            ContentRatings = first.ContentRatings.Count > 0 ? first.ContentRatings : second.ContentRatings,
            Previews = first.Previews.Count > 0 ? first.Previews : second.Previews,
            ExternalIds = first.ExternalIds.Concat(second.ExternalIds)
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase),
            SubjectReferences = [.. first.SubjectReferences.Concat(second.SubjectReferences)
                .GroupBy(reference => string.Join(
                    "|",
                    reference.SubjectKind,
                    reference.RelationshipKind,
                    reference.RelationshipQualifier,
                    string.Join(
                        ",",
                        reference.ExternalIds.OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value))))
                .Select(group => group.Last())],
            DetailsLoaded = first.DetailsLoaded || second.DetailsLoaded,
            SourcePlanNodeId = first.SourcePlanNodeId ?? second.SourcePlanNodeId,
            AppliedCriteria = MergeAppliedCriteria(first.AppliedCriteria, second.AppliedCriteria)
        };
    }

    private DiscoverRequest? MergeAppliedCriteria(DiscoverRequest? first, DiscoverRequest? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return first with
        {
            AppliedTextFilters = [.. first.AppliedTextFilters.Concat(second.AppliedTextFilters).Distinct()],
            AppliedPublishDateFilters = [.. first.AppliedPublishDateFilters.Concat(second.AppliedPublishDateFilters)
                .Distinct()],
            AppliedDurationFilters = [.. first.AppliedDurationFilters.Concat(second.AppliedDurationFilters).Distinct()],
            AppliedGenreFilters = [.. first.AppliedGenreFilters.Concat(second.AppliedGenreFilters).Distinct()],
            AppliedSubjectGroups = [.. first.AppliedSubjectGroups.Concat(second.AppliedSubjectGroups)
                .Distinct(ResolvedSubjectGroupComparer.Instance)],
            AppliedElementFilters = [.. first.AppliedElementFilters.Concat(second.AppliedElementFilters).Distinct()]
        };
    }

    private bool TryMapExternalContentIdSource(string source, out string externalSource, out string resultSource)
    {
        (externalSource, resultSource) = source.Trim().ToLowerInvariant() switch
        {
            "imdb" or "imdb_id" => ("imdb_id", "imdb"),
            "wikidata" or "wikidata_id" => ("wikidata_id", "wikidata"),
            "tvdb" or "tvdb_id" => ("tvdb_id", "tvdb"),
            _ => (string.Empty, string.Empty)
        };
        return externalSource.Length > 0;
    }

    private string ScanKey(FetchPlanNode fetch, RequestContext context, string sourceKind)
    {
        var language = context.Language.ToLowerInvariant();
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1}{2}:{3}{4}:{5}",
            fetch.Fingerprint.Length,
            fetch.Fingerprint,
            language.Length,
            language,
            sourceKind.Length,
            sourceKind);
    }

    private int DesiredRows(FilterPlanNode filter)
    {
        if (filter.MaximumResults is { } maximumResults)
        {
            return Math.Max(1, maximumResults) + 1;
        }

        var requested = Math.Max(1L, filter.RequestedPage) * SourcePageSize + 1;
        return requested > int.MaxValue ? int.MaxValue : (int)requested;
    }

    private async Task<List<SearchHit>> MaterializeAsync(
        IAsyncEnumerable<SearchHit> hits,
        int maximum,
        CancellationToken cancellationToken)
    {
        var materialized = new List<SearchHit>();
        await foreach (var hit in hits.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            materialized.Add(hit);
            if (materialized.Count >= maximum)
            {
                break;
            }
        }

        return materialized;
    }

    private IReadOnlyList<DiscoverSource> DiscoverSources(
        FetchPlanNode fetch,
        IReadOnlyList<ResolvedSubjectGroup> subjects,
        IReadOnlyDictionary<string, int> genreIds)
    {
        var sources = new List<DiscoverSource>
        {
            new()
            {
                Subjects = subjects,
                GenreSelections = new Dictionary<GenreFilter, IReadOnlyList<int>>()
            }
        };
        for (var groupIndex = 0; groupIndex < subjects.Count; groupIndex++)
        {
            var group = subjects[groupIndex];
            var parameter = fetch.DiscoverParameters.FirstOrDefault(candidate =>
                candidate.SubjectBindingKeys.Contains(group.BindingKey, StringComparer.Ordinal));
            if (group.Excluded || parameter is null)
            {
                continue;
            }

            var chunkSize = parameter.Combination == DiscoverCombination.And ? 1 : MaximumResolvedIdsPerDiscoverRequest;
            var distinctIds = group.Subjects.Select(subject => subject.Id).Distinct().ToList();
            if (distinctIds.Count <= chunkSize)
            {
                continue;
            }

            var variants = new List<ResolvedSubjectGroup>();
            for (var index = 0; index < distinctIds.Count; index += chunkSize)
            {
                var ids = distinctIds.Skip(index).Take(chunkSize).ToHashSet();
                variants.Add(group with
                {
                    Subjects = [.. group.Subjects.Where(subject => ids.Contains(subject.Id))]
                });
            }

            sources = [.. sources.SelectMany(source => variants.Select(variant => source with
            {
                Subjects = [.. source.Subjects.Select((candidate, index) => index == groupIndex
                    ? variant
                    : candidate)]
            }))];
        }

        foreach (var parameter in fetch.DiscoverParameters.Where(candidate =>
            candidate.Combination == DiscoverCombination.And))
        {
            foreach (var filter in parameter.GenreFilters)
            {
                var ids = filter.ResolveGenreIds(genreIds).Distinct().ToList();
                if (ids.Count <= 1)
                {
                    continue;
                }

                sources = [.. sources.SelectMany(source => ids.Select(id =>
                {
                    var selections = source.GenreSelections.ToDictionary(pair => pair.Key, pair => pair.Value);
                    selections[filter] = [id];
                    return source with
                    {
                        GenreSelections = selections
                    };
                }))];
            }
        }

        return sources;
    }

    private void MarkSkipped(PlanNode node, QueryTelemetry? telemetry, string reason)
    {
        if (telemetry?.TryVisitNode(node.Id) != true)
        {
            return;
        }

        telemetry.RecordPrunedNode();
        telemetry.Activity?.AddEvent(new ActivityEvent(
            "node.skipped",
            tags: new ActivityTagsCollection
            {
                ["magpie.query.node.id"] = node.Id,
                ["magpie.query.node.kind"] = node.NodeKind(),
                ["magpie.query.node.reason"] = reason
            }));
        foreach (var child in node.Children())
        {
            MarkSkipped(child, telemetry, reason);
        }
    }

    private SearchHit AttachAppliedSubjects(SearchHit hit, DiscoverRequest? appliedCriteria)
    {
        if (appliedCriteria is null || appliedCriteria.AppliedSubjectGroups.Count == 0)
        {
            return hit;
        }

        var references = hit.SubjectReferences.Concat(appliedCriteria.AppliedSubjectGroups
            .Where(group => group.Subjects.Count == 1)
            .SelectMany(group => group.Subjects)
            .Select(subject => subject.FromResolvedSubject()))
            .GroupBy(reference => string.Join(
                "|",
                reference.SubjectKind,
                reference.RelationshipKind,
                reference.ExternalIds.OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value)))
            .Select(group => group.First())
            .ToList();
        return hit with { SubjectReferences = references };
    }

    private DiscoverRequest NormalizeAppliedSubjectGroups(
        DiscoverRequest request,
        IReadOnlyList<ResolvedSubjectGroup> sourceGroups,
        IReadOnlyList<ResolvedSubjectGroup> originalGroups)
    {
        var applied = sourceGroups.Select((group, index) => (group, index))
            .Where(item => request.AppliedSubjectGroups.Contains(
                item.group,
                ResolvedSubjectGroupComparer.Instance))
            .Select(item => originalGroups[item.index])
            .ToHashSet();
        return request with { AppliedSubjectGroups = applied };
    }

    private bool NeedsDetailsForHit(FilterPlanNode filter, SearchHit hit, IReadOnlyList<ResolvedSubjectGroup> subjects)
    {
        if (hit.DetailsLoaded)
        {
            return false;
        }

        var applied = hit.AppliedCriteria;
        if (filter.DurationFilters.Any(criterion => applied?.AppliedDurationFilters.Contains(criterion) != true) ||
            filter.TextFilters.Any(criterion => criterion.Field == TextField.ProductionStatus &&
                applied?.AppliedTextFilters.Contains(criterion) != true) ||
            filter.ElementFilters.Any(criterion => applied?.AppliedElementFilters.Contains(criterion) != true))
        {
            return true;
        }

        if (filter.ContentIdFilters.Any(criterion => criterion.Excluded &&
            criterion.Alternatives.Any(id => !hit.ExternalIds.ContainsKey(id.Source))))
        {
            return true;
        }

        return subjects.Any(group => NeedsSubjectDetails(group, hit));
    }

    private bool NeedsCreditsForHit(SearchHit hit, IReadOnlyList<ResolvedSubjectGroup> subjects)
    {
        return subjects.Any(group => NeedsSubjectDetails(group, hit) &&
            (group.Subjects.Any(subject => subject.Role is not SubjectRole.Company
                and not SubjectRole.Network) ||
                group.Alternatives.SelectMany(alternative => alternative.Roles).Any(role =>
                    role is not SubjectRole.Company and not SubjectRole.Network)));
    }

    private bool NeedsSubjectDetails(ResolvedSubjectGroup group, SearchHit hit)
    {
        if (hit.AppliedCriteria?.AppliedSubjectGroups.Contains(group, ResolvedSubjectGroupComparer.Instance) == true)
        {
            return false;
        }

        var knownMatch = hit.SubjectReferences.Any(reference => group.Subjects.Any(subject =>
            reference.MatchesSubject(subject)));
        return group.Excluded || !knownMatch;
    }

    private bool NeedsElementDetails(FilterPlanNode filter, SearchHit hit, ElementFamily family)
    {
        return filter.ElementFilters.Any(criterion => criterion.Family == family &&
            hit.AppliedCriteria?.AppliedElementFilters.Contains(criterion) != true);
    }

    private Request BuildSearch(FetchPlanNode fetch, string language, int page, string? searchTerm)
    {
        var path = fetch.Route == ContentRoute.Movie ? "3/search/movie" : "3/search/tv";
        var query = new List<(string Key, string Value)>();
        Add(query, "query", searchTerm);
        Add(query, "language", language);

        var exactYear = fetch.PublishDateFilters.FirstOrDefault(filter =>
            !filter.Negate &&
            filter.Operator == CriteriaValueOperator.Equals &&
            filter.Values.Count == 1 &&
            filter.Values[0].Precision == TemporalPrecision.Year);
        if (exactYear is not null)
        {
            Add(
                query,
                fetch.Route == ContentRoute.Movie ? "primary_release_year" : "first_air_date_year",
                exactYear.Values[0].Value.Year.ToString(CultureInfo.InvariantCulture));
        }

        Add(query, "page", Math.Clamp(page, 1, QueryLimits.MaximumPage).ToString(CultureInfo.InvariantCulture));
        return Create(RequestRole.Search, path, query, ResponseProfile.Query);
    }

    private IReadOnlyList<string> SearchTerms(FetchPlanNode fetch)
    {
        if (fetch.OrdinarySearchTerm is { } ordinary)
        {
            return [ordinary];
        }

        return fetch.TextFilters
            .FirstOrDefault(filter => filter.Field is TextField.Title or TextField.OriginalTitle &&
                !filter.Negate &&
                filter.Values.Count > 0)?.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ??
            [];
    }

    private DiscoverRequest BuildDiscover(
        FetchPlanNode fetch,
        string language,
        IReadOnlyDictionary<string, int> genreIds,
        IReadOnlyList<ResolvedSubjectGroup> subjects,
        int page,
        IReadOnlyDictionary<GenreFilter, IReadOnlyList<int>>? genreSelections = null)
    {
        var path = fetch.Route == ContentRoute.Movie ? "3/discover/movie" : "3/discover/tv";
        var query = new List<(string Key, string Value)>
        {
            ("language", language),
            ("sort_by", "popularity.desc"),
            ("page", Math.Clamp(page, 1, QueryLimits.MaximumPage).ToString(CultureInfo.InvariantCulture))
        };
        var appliedTextFilters = new HashSet<TextFilter>();
        var appliedPublishDateFilters = new HashSet<TemporalFilter>();
        var appliedDurationFilters = new HashSet<DurationFilter>();
        var appliedGenreFilters = new HashSet<GenreFilter>();
        var appliedSubjectGroups = new HashSet<ResolvedSubjectGroup>();
        var appliedElementFilters = new HashSet<ElementFilter>();
        AddOriginalLanguage(fetch, query, appliedTextFilters);
        AddPublishDate(fetch, query, appliedPublishDateFilters);
        AddRuntime(fetch, query, appliedDurationFilters);
        AddGenres(fetch, genreIds, genreSelections, query, appliedGenreFilters);
        AddSubjects(fetch, subjects, query, appliedSubjectGroups);
        AddCertification(fetch, query, appliedElementFilters);
        return new DiscoverRequest
        {
            Request = Create(RequestRole.Discover, path, query, ResponseProfile.Query),
            AppliedTextFilters = appliedTextFilters,
            AppliedPublishDateFilters = appliedPublishDateFilters,
            AppliedDurationFilters = appliedDurationFilters,
            AppliedGenreFilters = appliedGenreFilters,
            AppliedSubjectGroups = appliedSubjectGroups,
            AppliedElementFilters = appliedElementFilters
        };
    }

    private Request BuildSubjectSearch(SubjectRole role, string name, string language, int page = 1)
    {
        var company = role is SubjectRole.Company or SubjectRole.Network;
        var query = new List<(string Key, string Value)>
        {
            ("query", name),
            ("page", Math.Clamp(page, 1, QueryLimits.MaximumPage).ToString(CultureInfo.InvariantCulture))
        };
        if (!company)
        {
            query.Add(("language", language));
        }

        return Create(
            company ? RequestRole.CompanySearch : RequestRole.PersonSearch,
            company ? "3/search/company" : "3/search/person",
            query,
            ResponseProfile.Query);
    }

    private Request BuildFind(string externalId, string externalSource, string language)
    {
        return Create(
            RequestRole.Find,
            "3/find/" + Uri.EscapeDataString(externalId),
            [("external_source", externalSource), ("language", language)],
            ResponseProfile.Query);
    }

    private Request BuildPersonCredits(int personId, string language)
    {
        return Create(
            RequestRole.TvCredits,
            string.Format(CultureInfo.InvariantCulture, "3/person/{0}/tv_credits", personId),
            [("language", language)],
            ResponseProfile.Query);
    }

    private Request BuildDetail(
        ContentRoute route,
        int id,
        string language,
        bool appendCredits,
        bool appendExternalIds = false,
        bool appendAlternativeTitles = false,
        bool appendContentRatings = false,
        bool appendVideos = false,
        bool appendImages = false)
    {
        var query = new List<(string Key, string Value)> { ("language", language) };
        var appends = new List<string>();
        if (appendCredits)
        {
            appends.Add("credits");
        }

        if (appendExternalIds)
        {
            appends.Add("external_ids");
        }

        if (appendAlternativeTitles)
        {
            appends.Add("alternative_titles");
        }

        if (appendContentRatings)
        {
            appends.Add(route == ContentRoute.Movie ? "release_dates" : "content_ratings");
        }

        if (appendVideos)
        {
            appends.Add("videos");
        }

        if (appendImages)
        {
            appends.Add("images");
        }

        if (appends.Count > 0)
        {
            query.Add(("append_to_response", string.Join(",", appends)));
        }

        return Create(
            route == ContentRoute.Movie ? RequestRole.MovieDetail : RequestRole.TvDetail,
            string.Format(CultureInfo.InvariantCulture, route == ContentRoute.Movie ? "3/movie/{0}" : "3/tv/{0}", id),
            query,
            ResponseProfile.Detail);
    }

    private Request BuildGenreList(ContentRoute route, string language)
    {
        return Create(
            route == ContentRoute.Movie ? RequestRole.MovieGenres : RequestRole.TvGenres,
            route == ContentRoute.Movie ? "3/genre/movie/list" : "3/genre/tv/list",
            [("language", language)],
            ResponseProfile.Query);
    }

    private void AddOriginalLanguage(
        FetchPlanNode fetch,
        List<(string Key, string Value)> query,
        ISet<TextFilter> appliedFilters)
    {
        var filter = fetch.TextFilters.FirstOrDefault();
        var value = filter?.Values[0];
        if (value is null)
        {
            return;
        }

        var dash = value.IndexOf('-');
        var primary = dash > 0 ? value.Substring(0, dash) : value;
        Add(query, "with_original_language", primary);
        appliedFilters.Add(filter!);
    }

    private void AddPublishDate(
        FetchPlanNode fetch,
        List<(string Key, string Value)> query,
        ISet<TemporalFilter> appliedFilters)
    {
        var prefix = fetch.Route == ContentRoute.Movie ? "primary_release_date" : "first_air_date";
        var yearKey = fetch.Route == ContentRoute.Movie ? "primary_release_year" : "first_air_date_year";
        var filters = fetch.PublishDateFilters;
        if (filters.Count == 0)
        {
            return;
        }

        if (filters.Count == 1 &&
            filters[0].Operator == CriteriaValueOperator.Equals &&
            filters[0].Values[0].Precision == TemporalPrecision.Year)
        {
            Set(query, yearKey, filters[0].Values[0].Value.Year.ToString(CultureInfo.InvariantCulture));
            appliedFilters.Add(filters[0]);
            return;
        }

        var lower = default(DateTime?);
        var upper = default(DateTime?);
        foreach (var filter in filters)
        {
            var value = filter.Values[0];
            var start = value.PeriodStart().Date;
            var end = value.PeriodEndExclusive().Date;
            switch (filter.Operator)
            {
                case CriteriaValueOperator.Equals:
                    lower = Max(lower, start);
                    upper = Min(upper, end.AddDays(-1));
                    break;
                case CriteriaValueOperator.GreaterThan:
                    lower = Max(lower, end);
                    break;
                case CriteriaValueOperator.GreaterThanOrEqual:
                    lower = Max(lower, start);
                    break;
                case CriteriaValueOperator.LessThan:
                    upper = Min(upper, start.AddDays(-1));
                    break;
                case CriteriaValueOperator.LessThanOrEqual:
                    upper = Min(upper, end.AddDays(-1));
                    break;
            }

            appliedFilters.Add(filter);
        }

        if (lower is { } lowerValue)
        {
            Set(query, prefix + ".gte", FormatDate(lowerValue));
        }

        if (upper is { } upperValue)
        {
            Set(query, prefix + ".lte", FormatDate(upperValue));
        }
    }

    private void AddRuntime(
        FetchPlanNode fetch,
        List<(string Key, string Value)> query,
        ISet<DurationFilter> appliedFilters)
    {
        var lower = default(int?);
        var upper = default(int?);
        foreach (var filter in fetch.DurationFilters)
        {
            var totalMinutes = filter.Values[0].TotalMinutes;
            var floorMinutes = (int)Math.Floor(totalMinutes);
            var ceilingMinutes = (int)Math.Ceiling(totalMinutes);
            switch (filter.Operator)
            {
                case CriteriaValueOperator.Equals:
                    lower = Max(lower, floorMinutes);
                    upper = Min(upper, floorMinutes);
                    break;
                case CriteriaValueOperator.GreaterThan:
                    lower = Max(lower, floorMinutes + 1);
                    break;
                case CriteriaValueOperator.GreaterThanOrEqual:
                    lower = Max(lower, ceilingMinutes);
                    break;
                case CriteriaValueOperator.LessThan:
                    upper = Min(upper, ceilingMinutes - 1);
                    break;
                case CriteriaValueOperator.LessThanOrEqual:
                    upper = Min(upper, floorMinutes);
                    break;
            }

            appliedFilters.Add(filter);
        }

        if (lower is { } lowerValue)
        {
            Set(query, "with_runtime.gte", lowerValue.ToString(CultureInfo.InvariantCulture));
        }

        if (upper is { } upperValue)
        {
            Set(query, "with_runtime.lte", upperValue.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void AddGenres(
        FetchPlanNode fetch,
        IReadOnlyDictionary<string, int> genreIds,
        IReadOnlyDictionary<GenreFilter, IReadOnlyList<int>>? genreSelections,
        List<(string Key, string Value)> query,
        ISet<GenreFilter> appliedFilters)
    {
        foreach (var parameter in fetch.DiscoverParameters.Where(parameter => parameter.GenreFilters.Count > 0))
        {
            var clauses = parameter.GenreFilters.Select(filter => genreSelections?.TryGetValue(
                filter,
                out var selected) == true
                ? selected
                : filter.ResolveGenreIds(genreIds)).ToList();
            var values = clauses.SelectMany(ids => ids).Where(id => id > 0).Distinct().ToList();
            if (values.Count == 0)
            {
                continue;
            }

            Add(query, parameter.Name, JoinIds(values, parameter.Combination));
            if (clauses.All(ids => ids.Count > 0))
            {
                foreach (var filter in parameter.GenreFilters)
                {
                    appliedFilters.Add(filter);
                }
            }
        }
    }

    private void AddSubjects(
        FetchPlanNode fetch,
        IReadOnlyList<ResolvedSubjectGroup> groups,
        List<(string Key, string Value)> query,
        ISet<ResolvedSubjectGroup> appliedGroups)
    {
        foreach (var parameter in fetch.DiscoverParameters.Where(parameter => parameter.SubjectBindingKeys.Count > 0))
        {
            var clauses = parameter.SubjectBindingKeys.Select(key => groups.FirstOrDefault(group =>
                group.BindingKey.Equals(key, StringComparison.Ordinal))).ToList();
            if (clauses.Any(group => group is null || group.Subjects.Count == 0 || !CanPushDownSubjectGroup(group)))
            {
                continue;
            }

            var resolved = clauses.ToList();
            var ids = resolved.SelectMany(group => group.Subjects).Select(subject => subject.Id)
                .Where(id => id > 0).Distinct().ToList();
            if (parameter.Name.Equals("with_networks", StringComparison.Ordinal) && ids.Count != 1)
            {
                continue;
            }

            Add(query, parameter.Name, JoinIds(ids, parameter.Combination));
            foreach (var group in resolved.Where(group => !group.Incomplete &&
                group.Subjects.All(subject => subject.Role is not SubjectRole.Director and not SubjectRole.Writer)))
            {
                appliedGroups.Add(group);
            }
        }
    }

    private bool CanPushDownSubjectGroup(ResolvedSubjectGroup group)
    {
        return group.Alternatives.Count == 0 ||
        group.Alternatives.All(alternative => alternative.Criteria is null ||
            alternative.Criteria.Subject.And.All(CandidateFilterExtensions.IsPositiveSearchableText) &&
            alternative.Criteria.ExternalIds.SomeFlat.All(externalId =>
                externalId.Source.And.All(CandidateFilterExtensions.IsPositiveEquality) &&
                externalId.Value.And.All(CandidateFilterExtensions.IsPositiveEquality)) &&
            alternative.Criteria.ExternalIds.None.Count == 0);
    }

    private void AddCertification(
        FetchPlanNode fetch,
        List<(string Key, string Value)> query,
        ISet<ElementFilter> appliedFilters)
    {
        if (fetch.Route != ContentRoute.Movie)
        {
            return;
        }

        foreach (var filter in fetch.ElementFilters)
        {
            if (!TryReadCertificationPair(filter.Alternatives[0], out var country, out var rating))
            {
                continue;
            }

            Add(query, "certification_country", country);
            Add(query, "certification", rating);
            appliedFilters.Add(filter);
            return;
        }
    }

    private bool TryReadCertificationPair(
        IReadOnlyList<ElementPredicate> alternative,
        out string? country,
        out string? rating)
    {
        country = null;
        rating = null;
        if (alternative.Count != 2)
        {
            return false;
        }

        foreach (var predicate in alternative)
        {
            if (predicate.Negate ||
                predicate.Operator is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In) ||
                predicate.StringValues.Count != 1)
            {
                return false;
            }

            if (predicate.Field == ElementField.RatingBody)
            {
                country = predicate.StringValues[0].ToUpperInvariant();
            }
            else if (predicate.Field == ElementField.RatingValue)
            {
                rating = predicate.StringValues[0];
            }
        }

        return country is not null && rating is not null;
    }

    private string JoinIds(IEnumerable<int> ids, DiscoverCombination combination)
    {
        return string.Join(
            combination == DiscoverCombination.And ? "," : "|",
            ids
                .Where(id => id > 0)
                .Distinct()
                .Select(id => id.ToString(CultureInfo.InvariantCulture)));
    }

    private T Max<T>(T? current, T candidate)
        where T : struct, IComparable<T>
    {
        return current is null || candidate.CompareTo(current.Value) > 0 ? candidate : current.Value;
    }

    private T Min<T>(T? current, T candidate)
        where T : struct, IComparable<T>
    {
        return current is null || candidate.CompareTo(current.Value) < 0 ? candidate : current.Value;
    }

    private Request Create(
        string endpoint,
        string path,
        IReadOnlyList<(string Key, string Value)> query,
        ResponseProfile profile)
    {
        var nonblank = query.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).ToList();
        var cacheKey = path + "?" + string.Join("&", nonblank.Select(pair => pair.Key + "=" + pair.Value));
        return new Request
        {
            Endpoint = endpoint,
            Path = path,
            QueryParameters = nonblank,
            CacheKey = cacheKey,
            ResponseProfile = profile
        };
    }

    private string FormatDate(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private void Add(List<(string Key, string Value)> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add((key, value));
        }
    }

    private void Set(List<(string Key, string Value)> query, string key, string value)
    {
        query.RemoveAll(pair => pair.Key == key);
        query.Add((key, value));
    }

    private async Task<IReadOnlyList<SubjectResolution>> ResolveNameAsync(
        SubjectRole role,
        SubjectIdentity identity,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestedName = identity.Name!;
            var exact = !identity.Negate &&
                identity.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In;
            var normalized = NormalizeName(requestedName);
            var resolved = new List<SubjectResolution>();
            var truncated = false;
            for (var page = 1; page <= QueryLimits.MaximumNameSearchPages; page++)
            {
                var request = BuildSubjectSearch(role, requestedName.Trim(), context.Language, page);
                var envelope = await client.GetAsync<SearchEnvelopeDto<NamedSearchHitDto>>(
                    context,
                    request,
                    cancellationToken).ConfigureAwait(false);
                var matches = envelope?.Results?
                    .Where(hit => hit.Id > 0 &&
                        !string.IsNullOrWhiteSpace(hit.Name) &&
                        (exact
                            ? NormalizeName(hit.Name) == normalized
                            : !identity.Negate &&
                                hit.Name.MatchesText(identity.Operator, [requestedName])))
                    .GroupBy(hit => hit.Id)
                    .Select(group => group.First())
                    .ToList()
                    ?? [];
                resolved.AddRange(matches.Select(match => new SubjectResolution
                {
                    Kind = SubjectResolutionKind.Resolved,
                    Id = match.Id,
                    Name = match.Name!.Trim()
                }));

                if (envelope is null || page >= envelope.TotalPages)
                {
                    break;
                }

                if (page == QueryLimits.MaximumNameSearchPages)
                {
                    truncated = true;
                }
            }

            var unique = resolved.GroupBy(result => result.Id).Select(group => group.First()).ToList();
            if (unique.Count == 0)
            {
                return [new SubjectResolution { Kind = SubjectResolutionKind.NotFound }];
            }

            if (truncated)
            {
                unique[0] = unique[0] with { Incomplete = true };
            }

            return unique;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Magpie.Models.UpstreamThrottledException)
        {
            throw;
        }
        catch (Exception)
        {
            return [new SubjectResolution
            {
                Kind = SubjectResolutionKind.Failed,
                Warning = string.Format(
                    CultureInfo.InvariantCulture,
                    "TMDb failed while resolving {0} subject '{1}'.",
                    role == SubjectRole.Company ? "company" : "person",
                    identity.Name!.Trim())
            }];
        }
    }

    private async Task<SubjectResolution> ResolveExternalAsync(
        Request request,
        string externalIdSource,
        string externalId,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await client.GetAsync<FindEnvelopeDto>(context, request, cancellationToken)
                .ConfigureAwait(false);
            var matches = envelope?.PersonResults?
                .Where(result => result.Id > 0 && !string.IsNullOrWhiteSpace(result.Name))
                .GroupBy(result => result.Id)
                .Select(group => group.First())
                .ToList()
                ?? [];
            if (matches.Count == 0)
            {
                return new SubjectResolution { Kind = SubjectResolutionKind.NotFound };
            }

            if (matches.Count > 1)
            {
                return new SubjectResolution
                {
                    Kind = SubjectResolutionKind.Ambiguous,
                    Warning = string.Format(
                        CultureInfo.InvariantCulture,
                        "TMDb found multiple people for external identity {0}. No person was selected.",
                        externalId)
                };
            }

            return new SubjectResolution
            {
                Kind = SubjectResolutionKind.Resolved,
                Id = matches[0].Id,
                Name = matches[0].Name!.Trim(),
                ExternalIdSource = externalIdSource,
                ExternalId = externalId
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Magpie.Models.UpstreamThrottledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new SubjectResolution
            {
                Kind = SubjectResolutionKind.Failed,
                Warning = string.Format(
                    CultureInfo.InvariantCulture,
                    "TMDb failed while resolving external person identity {0}.",
                    externalId)
            };
        }
    }

    private bool TryDirectId(SubjectIdentity identity, out SubjectResolution resolution)
    {
        resolution = new SubjectResolution { Kind = SubjectResolutionKind.NotFound };
        if (!Constants.ProviderType.Equals(identity.ExternalIdSource, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(identity.ExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
            id <= 0)
        {
            return false;
        }

        resolution = new SubjectResolution
        {
            Kind = SubjectResolutionKind.Resolved,
            Id = id,
            Name = identity.Name ?? identity.ExternalId,
            ExternalIdSource = identity.ExternalIdSource,
            ExternalId = identity.ExternalId
        };
        return true;
    }

    private bool TryExternalSource(string source, out string tmdbSource)
    {
        tmdbSource = source.Trim().ToLowerInvariant() switch
        {
            "imdb" or "imdb_id" => "imdb_id",
            "wikidata" or "wikidata_id" => "wikidata_id",
            _ => string.Empty
        };
        return tmdbSource.Length > 0;
    }

    private string RoleNoun(SubjectRole role)
    {
        return role switch
        {
            SubjectRole.Company => "company",
            SubjectRole.Network => "network",
            _ => "person"
        };
    }

    private string NormalizeName(string name)
    {
        return string.Join(
            " ",
            name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }

    private IReadOnlyDictionary<string, string>? ExternalIds(SubjectResolution resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution.ExternalIdSource) || string.IsNullOrWhiteSpace(resolution.ExternalId))
        {
            return null;
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [resolution.ExternalIdSource] = resolution.ExternalId
        };
    }

    private async Task<QueryExecution> ExecuteNodeAsync(
        PlanNode node,
        QueryPlan plan,
        RequestContext context,
        ExecutionState state,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        switch (node)
        {
            case FetchPlanNode fetch:
                if (fetch.Resolve is not null)
                {
                    await ExecuteResolveNodeAsync(
                        fetch.Resolve,
                        context,
                        state,
                        warnings,
                        cancellationToken).ConfigureAwait(false);
                }

                if (!PreconditionSatisfied(fetch.Precondition, state))
                {
                    MarkSkipped(fetch, context.Telemetry, "precondition unsatisfied");
                    return LogicalResult([], warnings, plan.RequestedPage);
                }

                if (state.ProvablyEmpty)
                {
                    MarkSkipped(fetch, context.Telemetry, "resolved anchor is empty");
                    return LogicalResult([], warnings, plan.RequestedPage);
                }

                return await ExecuteFetchNodeAsync(
                    fetch,
                    context,
                    state.GenreIds,
                    state.SubjectSet.Groups,
                    warnings,
                    cancellationToken).ConfigureAwait(false);
            case FilterPlanNode filter:
                {
                    var child = await ExecuteNodeAsync(
                        filter.Child,
                        plan,
                        context,
                        state,
                        warnings,
                        cancellationToken).ConfigureAwait(false);
                    return child with
                    {
                        Hits = FilterHitsAsync(filter, child.Hits, context, state, cancellationToken)
                    };
                }
            case UnionPlanNode union:
                return LogicalResult(
                    UnionHitsAsync(union, plan, context, state, warnings, cancellationToken),
                    warnings,
                    plan.RequestedPage);
            case IntersectPlanNode intersect:
                {
                    if (context.Progress?.PendingIntersections.TryGetValue(intersect.Id, out var pending) == true)
                    {
                        return LogicalResult(ConsumePendingAsync(pending), warnings, plan.RequestedPage);
                    }

                    context.Telemetry?.TryVisitNode(intersect.Id);
                    using var intersectActivity = Telemetry.Source.StartChild(
                        "tmdb.query.node.intersect",
                        context.Telemetry?.Activity);
                    intersectActivity?.SetTag("magpie.query.node.id", intersect.Id);
                    intersectActivity?.SetTag("magpie.query.branch_count", intersect.Children.Count);
                    var childResults = new List<IReadOnlyList<SearchHit>>(intersect.Children.Count);
                    foreach (var childNode in intersect.Children)
                    {
                        var values = context.Progress?.Evaluation.GetValueOrDefault(childNode.Id) ?? [];
                        if (context.Progress?.CompleteEvaluations.Contains(childNode.Id) == true)
                        {
                            childResults.Add(values);
                            continue;
                        }

                        if (context.Progress is { } evaluationProgress)
                        {
                            evaluationProgress.Evaluation[childNode.Id] = values;
                        }

                        var warningCount = warnings.Count;
                        var child = await ExecuteNodeAsync(
                            childNode,
                            plan,
                            context,
                            state,
                            warnings,
                            cancellationToken).ConfigureAwait(false);
                        var identities = values.Select(hit => (hit.Route, hit.Id)).ToHashSet();
                        await foreach (var hit in child.Hits.WithCancellation(cancellationToken).ConfigureAwait(false))
                        {
                            if (identities.Add((hit.Route, hit.Id)))
                            {
                                values.Add(hit);
                            }
                        }

                        if (context.Progress is not null && warnings.Count > warningCount)
                        {
                            return LogicalResult([], warnings, plan.RequestedPage);
                        }

                        context.Progress?.CompleteEvaluations.Add(childNode.Id);
                        childResults.Add(values);
                    }

                    var hits = childResults.Count == 0
                        ? []
                        : childResults.Skip(1).Aggregate(
                            childResults[0].ToList(),
                            (current, next) => IntersectHits(current, next));
                    intersectActivity?.SetTag("magpie.query.rows_out", hits.Count);
                    if (context.Progress is { } progress)
                    {
                        progress.PendingIntersections[intersect.Id] = hits;
                        return LogicalResult(ConsumePendingAsync(hits), warnings, plan.RequestedPage);
                    }

                    return LogicalResult(hits, warnings, plan.RequestedPage);
                }
            case CoalescePlanNode coalesce:
                {
                    for (var index = 0; index < coalesce.Children.Count; index++)
                    {
                        var childNode = coalesce.Children[index];
                        await EnsurePrerequisitesAsync(
                            childNode,
                            plan,
                            context,
                            state,
                            warnings,
                            cancellationToken).ConfigureAwait(false);
                        if (!PreconditionSatisfied(childNode.Precondition, state))
                        {
                            MarkSkipped(childNode, context.Telemetry, "precondition unsatisfied");
                            continue;
                        }

                        foreach (var skipped in coalesce.Children.Skip(index + 1))
                        {
                            MarkSkipped(skipped, context.Telemetry, "coalesce already satisfied");
                        }

                        context.Telemetry?.TryVisitNode(coalesce.Id);
                        using var coalesceActivity = Telemetry.Source.StartChild(
                            "tmdb.query.node.coalesce",
                            context.Telemetry?.Activity);
                        coalesceActivity?.SetTag("magpie.query.node.id", coalesce.Id);
                        coalesceActivity?.SetTag("magpie.query.branch_count", coalesce.Children.Count);
                        return await ExecuteNodeAsync(
                            childNode,
                            plan,
                            context,
                            state,
                            warnings,
                            cancellationToken).ConfigureAwait(false);
                    }

                    return LogicalResult([], warnings, plan.RequestedPage);
                }
            case ResolvePlanNode resolve:
                await ExecuteResolveNodeAsync(
                    resolve,
                    context,
                    state,
                    warnings,
                    cancellationToken).ConfigureAwait(false);
                return LogicalResult([], warnings, plan.RequestedPage);
            default:
                throw new ArgumentOutOfRangeException(nameof(node));
        }
    }

    private async Task<QueryExecution> ExecuteFetchNodeAsync(
        FetchPlanNode fetch,
        RequestContext context,
        IReadOnlyDictionary<string, int> genreIds,
        IReadOnlyList<ResolvedSubjectGroup> subjects,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var fetchSubjects = SelectSubjectGroups(fetch, subjects);
        var originalSubjects = SelectOriginalSubjectGroups(fetch, subjects);
        var fetchContext = context with { PlanNodeId = fetch.Id };
        var execution = fetch.AnchorKind switch
        {
            QueryAnchorKind.Search => await ExecuteSearchAsync(
                fetch,
                fetchContext,
                genreIds,
                fetchSubjects,
                warnings,
                cancellationToken).ConfigureAwait(false),
            QueryAnchorKind.Discover => await ExecuteDiscoverAsync(
                fetch,
                fetchContext,
                genreIds,
                fetchSubjects,
                warnings,
                cancellationToken).ConfigureAwait(false),
            QueryAnchorKind.PersonCredits => await ExecuteCreditsAsync(
                fetch,
                fetchContext,
                fetchSubjects,
                warnings,
                cancellationToken).ConfigureAwait(false),
            QueryAnchorKind.ContentIds => await ExecuteContentIdsAsync(
                fetch,
                fetchContext,
                warnings,
                cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(fetch))
        };
        return execution with
        {
            Hits = FetchNodeHitsAsync(
                fetch,
                NormalizeFetchHitsAsync(execution.Hits, fetchSubjects, originalSubjects, cancellationToken),
                execution.SourceHasMore,
                context.Telemetry,
                cancellationToken)
        };
    }

    private IReadOnlyList<ResolvedSubjectGroup> SelectSubjectGroups(
        FetchPlanNode fetch,
        IReadOnlyList<ResolvedSubjectGroup> subjects)
    {
        if (fetch.IgnoreSubjectFilters)
        {
            return [];
        }

        return [.. fetch.SubjectBindings.Select(binding => subjects.FirstOrDefault(group =>
            group.BindingKey.Equals(binding.Key, StringComparison.Ordinal)))
            .Where(group => group is not null)
            .Select(item =>
            {
                var selectedRoles = fetch.SubjectRoleSelections
                    .FirstOrDefault(selection => selection.SubjectBindingKey == item.BindingKey)?.Roles
                    ?? fetch.SubjectRoles;
                return selectedRoles.Count == 0
                    ? item!
                    : item with
                    {
                        Subjects = [.. item.Subjects.Where(subject => selectedRoles.Contains(subject.Role))],
                        Alternatives = [.. item.Alternatives.Where(alternative =>
                            alternative.Roles.Any(selectedRoles.Contains))],
                        Incomplete = selectedRoles.Any(item.IncompleteRoles.Contains),
                        IncompleteRoles = item.IncompleteRoles.Where(selectedRoles.Contains).ToHashSet()
                    };
            })];
    }

    private IReadOnlyList<ResolvedSubjectGroup> SelectOriginalSubjectGroups(
        FetchPlanNode fetch,
        IReadOnlyList<ResolvedSubjectGroup> subjects)
    {
        if (fetch.IgnoreSubjectFilters)
        {
            return [];
        }

        return [.. fetch.SubjectBindings.Select(binding => subjects.FirstOrDefault(group =>
            group.BindingKey.Equals(binding.Key, StringComparison.Ordinal)))
            .OfType<ResolvedSubjectGroup>()];
    }

    private async IAsyncEnumerable<SearchHit> NormalizeFetchHitsAsync(
        IAsyncEnumerable<SearchHit> hits,
        IReadOnlyList<ResolvedSubjectGroup> sourceGroups,
        IReadOnlyList<ResolvedSubjectGroup> originalGroups,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var hit in hits.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return hit.AppliedCriteria is null
                ? hit
                : hit with
                {
                    AppliedCriteria = NormalizeAppliedSubjectGroups(hit.AppliedCriteria, sourceGroups, originalGroups)
                };
        }
    }

    private async IAsyncEnumerable<SearchHit> FetchNodeHitsAsync(
        FetchPlanNode fetch,
        IAsyncEnumerable<SearchHit> hits,
        bool hasMore,
        QueryTelemetry? telemetry,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        telemetry?.TryVisitNode(fetch.Id);
        using var activity = Telemetry.Source.StartChild("tmdb.query.node.fetch", telemetry?.Activity);
        activity?.SetTag("magpie.query.node.id", fetch.Id);
        activity?.SetTag("magpie.query.anchor_kind", fetch.AnchorKind.AnchorKindName());
        activity?.SetTag("magpie.query.has_more", hasMore);
        var count = 0;
        try
        {
            await foreach (var hit in hits.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                count++;
                yield return hit with { SourcePlanNodeId = fetch.Id };
            }
        }
        finally
        {
            activity?.SetTag("magpie.query.rows_fetched", count);
        }
    }

    private async IAsyncEnumerable<SearchHit> FilterHitsAsync(
        FilterPlanNode filter,
        IAsyncEnumerable<SearchHit> hits,
        RequestContext context,
        ExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        context.Telemetry?.TryVisitNode(filter.Id);
        using var activity = Telemetry.Source.StartChild("tmdb.query.node.filter", context.Telemetry?.Activity);
        activity?.SetTag("magpie.query.node.id", filter.Id);
        var rejections = new Dictionary<string, int>(StringComparer.Ordinal);
        var rejected = activity is null
            ? null
            : new Action<string>(predicate => rejections[predicate] = rejections.GetValueOrDefault(predicate) + 1);
        var rowsIn = 0;
        var rowsOut = 0;
        var desiredRows = context.Progress is null ? DesiredRows(filter) : int.MaxValue;
        var filterContext = context with { PlanNodeId = filter.Id };
        try
        {
            await foreach (var sourceHit in hits.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                rowsIn++;
                var needsDetails = NeedsDetailsForHit(filter, sourceHit, state.SubjectSet.Groups);
                if (!sourceHit.Matches(
                    filter,
                    state.GenreIds,
                    state.SubjectSet.Groups,
                    deferDetailFilters: needsDetails,
                    appliedCriteria: sourceHit.AppliedCriteria,
                    imageBaseUrl: state.ImageBase,
                    rejected: rejected))
                {
                    continue;
                }

                var hit = sourceHit;
                if (needsDetails)
                {
                    hit = await EnrichAsync(
                        filter,
                        filterContext,
                        sourceHit,
                        appendCredits: NeedsCreditsForHit(sourceHit, state.SubjectSet.Groups),
                        cancellationToken).ConfigureAwait(false);
                    if (hit is null ||
                        !hit.Matches(
                            filter,
                            state.GenreIds,
                            state.SubjectSet.Groups,
                            appliedCriteria: hit.AppliedCriteria,
                            imageBaseUrl: state.ImageBase,
                            rejected: rejected))
                    {
                        continue;
                    }
                }

                rowsOut++;
                if (rowsOut >= desiredRows)
                {
                    state.ResultLimitSatisfied = true;
                }

                yield return hit;
                if (state.ResultLimitSatisfied)
                {
                    yield break;
                }
            }
        }
        finally
        {
            if (activity is not null)
            {
                activity.SetTag("magpie.query.filter.rows_in", rowsIn);
                activity.SetTag("magpie.query.filter.rows_out", rowsOut);
                if (rejections.Count > 0)
                {
                    var tags = new ActivityTagsCollection();
                    foreach (var rejection in rejections)
                    {
                        tags["magpie.query.filter.rejected." + rejection.Key] = rejection.Value;
                    }

                    activity.AddEvent(new ActivityEvent("filter.rejections", tags: tags));
                }
            }
        }
    }

    private async IAsyncEnumerable<SearchHit> UnionHitsAsync(
        UnionPlanNode union,
        QueryPlan plan,
        RequestContext context,
        ExecutionState state,
        List<string> warnings,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        context.Telemetry?.TryVisitNode(union.Id);
        using var activity = Telemetry.Source.StartChild("tmdb.query.node.union", context.Telemetry?.Activity);
        activity?.SetTag("magpie.query.node.id", union.Id);
        activity?.SetTag("magpie.query.branch_count", union.Children.Count);
        var emitted = new HashSet<(ContentRoute Route, int Id)>();
        var visitedChildren = 0;
        try
        {
            foreach (var childNode in union.Children)
            {
                visitedChildren++;
                var child = await ExecuteNodeAsync(
                    childNode,
                    plan,
                    context,
                    state,
                    warnings,
                    cancellationToken).ConfigureAwait(false);
                await foreach (var hit in child.Hits.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    if (emitted.Add((hit.Route, hit.Id)))
                    {
                        yield return hit;
                    }
                }
            }
        }
        finally
        {
            var reason = state.ResultLimitSatisfied
                ? "union result limit was satisfied"
                : cancellationToken.IsCancellationRequested
                    ? "union enumeration was cancelled"
                    : "union enumeration ended before this child was visited";
            foreach (var unvisitedChild in union.Children.Skip(visitedChildren))
            {
                MarkSkipped(unvisitedChild, context.Telemetry, reason);
            }

            activity?.SetTag("magpie.query.rows_out", emitted.Count);
        }
    }

    private async Task EnsurePrerequisitesAsync(
        PlanNode node,
        QueryPlan plan,
        RequestContext context,
        ExecutionState state,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        foreach (var resolve in node.Children().SelectMany(FindResolveNodes)
            .GroupBy(candidate => candidate.Id).Select(group => group.First()))
        {
            await ExecuteResolveNodeAsync(
                resolve,
                context,
                state,
                warnings,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private IEnumerable<ResolvePlanNode> FindResolveNodes(PlanNode node)
    {
        if (node is ResolvePlanNode resolve)
        {
            yield return resolve;
        }

        foreach (var child in node.Children())
        {
            foreach (var nested in FindResolveNodes(child))
            {
                yield return nested;
            }
        }
    }

    private async Task ExecuteResolveNodeAsync(
        ResolvePlanNode resolve,
        RequestContext context,
        ExecutionState state,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (state.ResolvedNodeIds.Contains(resolve.Id))
        {
            return;
        }

        context.Telemetry?.TryVisitNode(resolve.Id);
        using var activity = Telemetry.Source.StartChild("tmdb.query.node.resolve", context.Telemetry?.Activity);
        activity?.SetTag("magpie.query.node.id", resolve.Id);
        var resolveContext = context with { PlanNodeId = resolve.Id };
        var subjectSet = resolve.SubjectBindings.Count == 0
            ? EmptySubjectSet
            : await ResolveAsync(resolve, resolveContext, cancellationToken).ConfigureAwait(false);
        warnings.AddRange(subjectSet.Warnings);
        state.SubjectSet = subjectSet;
        if (resolve.GenreFilters.Count > 0)
        {
            state.GenreIds = await ResolveGenresAsync(resolve, resolveContext, cancellationToken)
                .ConfigureAwait(false);
        }

        if (resolve.ResolveImageBase)
        {
            state.ImageBase = await cache.GetImageBaseUrlAsync(
                resolveContext,
                client,
                logger,
                cancellationToken).ConfigureAwait(false);
        }

        state.ProvablyEmpty = PositiveGenreCannotMatch(resolve, state.GenreIds) ||
            subjectSet.RequiredGroupUnresolved;
        state.ResolvedNodeIds.Add(resolve.Id);
        if (activity is not null)
        {
            activity.SetTag(
                "magpie.query.rows_out",
                subjectSet.Groups.Sum(group => group.Subjects.Count) + state.GenreIds.Count);
            if (subjectSet.HadRemoteFailure)
            {
                activity.SetStatus(ActivityStatusCode.Error);
            }
        }
    }

    private bool PreconditionSatisfied(PlanPrecondition precondition, ExecutionState state)
    {
        return precondition.Kind switch
        {
            PlanPreconditionKind.None => true,
            PlanPreconditionKind.NodeNonEmpty => !state.SubjectSet.RequiredGroupUnresolved,
            PlanPreconditionKind.NodeApplicable => !state.SubjectSet.HadRemoteFailure,
            PlanPreconditionKind.NodeFailed => state.SubjectSet.HadRemoteFailure,
            _ => false
        };
    }

    private async Task<QueryExecution> ExecuteSearchAsync(
        FetchPlanNode fetch,
        RequestContext context,
        IReadOnlyDictionary<string, int> genreIds,
        IReadOnlyList<ResolvedSubjectGroup> subjects,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (fetch.OrdinarySearchTerm is not null)
        {
            var request = BuildSearch(
                fetch,
                context.Language,
                fetch.RequestedPage,
                SearchTerms(fetch).FirstOrDefault());
            var page = await FetchPageAsync(fetch.Route, context, request, cancellationToken).ConfigureAwait(false);
            return SourceResult(page.Hits, warnings, fetch.RequestedPage, fetch.RequestedPage < page.TotalPages);
        }

        return LogicalResult(
            ScanAsync(
                fetch,
                context,
                genreIds,
                subjects,
                sourceKind: "search",
                discover: false,
                warnings,
                cancellationToken),
            warnings,
            fetch.RequestedPage);
    }

    private async Task<QueryExecution> ExecuteDiscoverAsync(
        FetchPlanNode fetch,
        RequestContext context,
        IReadOnlyDictionary<string, int> genreIds,
        IReadOnlyList<ResolvedSubjectGroup> subjects,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var sources = DiscoverSources(fetch, subjects, genreIds);
        var source = sources[0];
        var discover = BuildDiscover(
            fetch,
            context.Language,
            genreIds,
            source.Subjects,
            fetch.RequestedPage,
            source.GenreSelections);
        if (context.Progress is null && !fetch.RequiresLogicalStream &&
            !fetch.IsIntersectionBranch &&
            sources.Count == 1 &&
            IsFullyApplied(fetch, discover, source.Subjects))
        {
            var page = await FetchPageAsync(fetch.Route, context, discover.Request, cancellationToken)
                .ConfigureAwait(false);
            return SourceResult(
                [.. page.Hits.Select(hit => AttachAppliedSubjects(hit, discover) with
                {
                    AppliedCriteria = discover
                })],
                warnings,
                fetch.RequestedPage,
                fetch.RequestedPage < page.TotalPages);
        }

        return LogicalResult(
            ScanAsync(
                fetch,
                context,
                genreIds,
                subjects,
                sourceKind: "discover",
                discover: true,
                warnings,
                cancellationToken),
            warnings,
            fetch.RequestedPage);
    }

    private async Task<QueryExecution> ExecuteCreditsAsync(
        FetchPlanNode fetch,
        RequestContext context,
        IReadOnlyList<ResolvedSubjectGroup> subjects,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var positivePersonGroups = subjects.Where(group => !group.Excluded &&
            group.Subjects.Any(subject =>
                subject.Role is not SubjectRole.Company and not SubjectRole.Network)).ToList();
        var groupHits = new List<IReadOnlyList<SearchHit>>();
        foreach (var group in positivePersonGroups)
        {
            var alternatives = new List<SearchHit>();
            foreach (var subject in group.Subjects.Where(subject =>
                subject.Role is not SubjectRole.Company and not SubjectRole.Network))
            {
                var credits = await GetCreditsAsync(subject, context, cancellationToken)
                    .ConfigureAwait(false);
                alternatives.AddRange(MapCredits(credits, subject));
            }

            groupHits.Add(MergeHits(alternatives));
        }

        var hits = groupHits.Count == 0
            ? []
            : groupHits.Skip(1).Aggregate(groupHits[0].ToList(), (current, next) => IntersectHits(current, next));
        return LogicalResult(hits, warnings, fetch.RequestedPage);
    }

    private async Task<QueryExecution> ExecuteContentIdsAsync(
        FetchPlanNode fetch,
        RequestContext context,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var positives = fetch.ContentIdFilters.Where(filter => !filter.Excluded).ToList();
        if (positives.Count == 0)
        {
            return LogicalResult([], warnings, fetch.RequestedPage);
        }

        var groupHits = new List<List<SearchHit>>(positives.Count);
        foreach (var positive in positives)
        {
            var fetchedGroup = await FetchContentIdGroupAsync(
                fetch,
                context,
                positive,
                appendCredits: HasPersonSubjects(fetch.SubjectBindings, []),
                cancellationToken)
                .ConfigureAwait(false);
            groupHits.Add(fetchedGroup);
        }

        var fetched = groupHits.Skip(1).Aggregate(groupHits[0], (current, next) => IntersectHits(current, next));

        return LogicalResult(fetched, warnings, fetch.RequestedPage);
    }

    private async Task<List<SearchHit>> FetchContentIdGroupAsync(
        FetchPlanNode fetch,
        RequestContext context,
        ContentIdFilter filter,
        bool appendCredits,
        CancellationToken cancellationToken)
    {
        using var concurrency = new SemaphoreSlim(MaximumConcurrency, MaximumConcurrency);
        var tasks = filter.Alternatives.Select(async (id, order) =>
        {
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await FetchContentIdAsync(fetch, context, id, order, appendCredits, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                concurrency.Release();
            }
        }).ToList();
        var lookups = await Task.WhenAll(tasks).ConfigureAwait(false);
        var resolved = lookups
            .OfType<SearchHit>()
            .ToList();
        var hits = resolved
            .GroupBy(hit => hit.Id)
            .Select(group => group.First())
            .OrderBy(hit => hit.SourceOrder)
            .ToList();
        return hits;
    }

    private async IAsyncEnumerable<SearchHit> ScanAsync(
        FetchPlanNode fetch,
        RequestContext context,
        IReadOnlyDictionary<string, int> genreIds,
        IReadOnlyList<ResolvedSubjectGroup> subjects,
        string sourceKind,
        bool discover,
        List<string> warnings,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (context.Progress is not null)
        {
            await foreach (var hit in ContinueScanAsync(
                fetch, context, genreIds, subjects, discover, warnings, cancellationToken).ConfigureAwait(false))
            {
                yield return hit;
            }

            yield break;
        }

        var branchSourceKind = fetch.BranchScopedScan
            ? string.Format(
                CultureInfo.InvariantCulture,
                "{0}:node:{1}",
                sourceKind,
                context.PlanNodeId?.ToString(CultureInfo.InvariantCulture) ?? "root")
            : sourceKind;
        var state = cache.GetScanState(ScanKey(fetch, context, branchSourceKind));
        var searchTerms = discover ? [] : SearchTerms(fetch);
        var discoverSources = discover ? DiscoverSources(fetch, subjects, genreIds) : [];
        var sourceCount = discover ? discoverSources.Count : searchTerms.Count;
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var yielded = 0;
            while (true)
            {
                while (yielded < state.Hits.Count)
                {
                    yield return state.Hits[yielded++];
                }

                if (state.Exhausted)
                {
                    yield break;
                }

                if (SourcePageLimitReached(state, warnings))
                {
                    yield break;
                }

                if (sourceCount == 0 || state.ExhaustedSourceIndexes.Count >= sourceCount)
                {
                    state.Exhausted = true;
                    yield break;
                }

                DiscoverRequest? appliedCriteria = null;
                Request request;
                if (discover)
                {
                    var source = discoverSources[state.NextSourceIndex];
                    var sourceGroups = source.Subjects;
                    var built = BuildDiscover(
                        fetch,
                        context.Language,
                        genreIds,
                        sourceGroups,
                        state.NextSourcePage,
                        source.GenreSelections);
                    built = NormalizeAppliedSubjectGroups(built, sourceGroups, subjects);
                    request = built.Request;
                    appliedCriteria = built;
                }
                else
                {
                    request = BuildSearch(
                        fetch,
                        context.Language,
                        state.NextSourcePage,
                        searchTerms[state.NextSourceIndex]);
                }

                var remote = await FetchPageAsync(fetch.Route, context, request, cancellationToken)
                    .ConfigureAwait(false);
                var hits = remote.Hits.Select(hit => AttachAppliedSubjects(hit, appliedCriteria) with
                {
                    AppliedCriteria = appliedCriteria
                }).ToList();
                if (sourceCount > 1)
                {
                    state.RoundBatches.Add(hits);
                    if (hits.Count > 0)
                    {
                        AddOrMerge(state.Hits, hits[0]);
                    }
                }
                else
                {
                    foreach (var hit in hits)
                    {
                        AddOrMerge(state.Hits, hit);
                    }
                }

                if (remote.Hits.Count == 0 || state.NextSourcePage >= remote.TotalPages)
                {
                    state.ExhaustedSourceIndexes.Add(state.NextSourceIndex);
                }

                var roundComplete = AdvanceSearchSource(state, sourceCount);
                if (sourceCount > 1 && (roundComplete || state.Exhausted))
                {
                    foreach (var hit in InterleaveHits(state.RoundBatches))
                    {
                        AddOrMerge(state.Hits, hit);
                    }

                    state.RoundBatches.Clear();
                }
            }
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private async IAsyncEnumerable<SearchHit> ConsumePendingAsync(List<SearchHit> pending)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        while (pending.Count > 0)
        {
            yield return pending[0];
            pending.RemoveAt(0);
        }
    }

    private async IAsyncEnumerable<SearchHit> ContinueScanAsync(
        FetchPlanNode fetch,
        RequestContext context,
        IReadOnlyDictionary<string, int> genreIds,
        IReadOnlyList<ResolvedSubjectGroup> subjects,
        bool discover,
        List<string> warnings,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var progress = context.Progress!;
        if (!progress.Sources.TryGetValue(fetch.Id, out var state))
        {
            state = new SourceProgress();
            progress.Sources.Add(fetch.Id, state);
        }

        var searchTerms = discover ? [] : SearchTerms(fetch);
        var sources = discover ? DiscoverSources(fetch, subjects, genreIds) : [];
        var sourceCount = discover ? sources.Count : searchTerms.Count;
        var signature = ContinuedSourceSignature(fetch, context, genreIds, subjects, discover);
        if (state.Signature is not null && state.Signature != signature)
        {
            throw new MetadataSearchContinuationException("TMDb source order or identities changed.");
        }

        state.Signature = signature;
        while (!state.Exhausted)
        {
            if (state.Page > QueryLimits.MaximumPage)
            {
                warnings.Add(QueryLimits.PageLimitWarning);
                yield break;
            }

            var batches = new List<IReadOnlyList<SearchHit>>();
            var hasMore = false;
            var ended = new HashSet<int>(state.ExhaustedSources);
            for (var index = 0; index < sourceCount; index++)
            {
                if (state.ExhaustedSources.Contains(index))
                {
                    batches.Add([]);
                    if (state.Consumed == index)
                    {
                        state.Consumed++;
                    }

                    continue;
                }

                var applied = discover
                    ? BuildDiscover(
                        fetch,
                        context.Language,
                        genreIds,
                        sources[index].Subjects,
                        state.Page,
                        sources[index].GenreSelections)
                    : null;
                if (applied is not null)
                {
                    applied = NormalizeAppliedSubjectGroups(applied, sources[index].Subjects, subjects);
                }

                var request = applied?.Request ?? BuildSearch(fetch, context.Language, state.Page, searchTerms[index]);
                var remote = await FetchPageAsync(fetch.Route, context, request, cancellationToken)
                    .ConfigureAwait(false);
                hasMore |= state.Page < remote.TotalPages;
                if (state.Page >= remote.TotalPages)
                {
                    ended.Add(index);
                }

                batches.Add([.. remote.Hits.Select(hit => AttachAppliedSubjects(hit, applied) with
                {
                    AppliedCriteria = applied
                })]);
                if (state.Consumed == index)
                {
                    var first = batches[index].FirstOrDefault();
                    if (first is not null && !state.Seen.Contains(first.Id))
                    {
                        yield return first;
                        state.Seen.Add(first.Id);
                    }

                    state.Consumed++;
                }
            }

            var round = batches.Select(batch => batch.FirstOrDefault()).Concat(InterleaveHits(batches)).ToArray();
            while (state.Consumed < round.Length)
            {
                var hit = round[state.Consumed];
                if (hit is not null && !state.Seen.Contains(hit.Id))
                {
                    yield return hit;
                    state.Seen.Add(hit.Id);
                }

                state.Consumed++;
            }

            if (!hasMore)
            {
                state.Exhausted = true;
                yield break;
            }

            if (state.Page >= QueryLimits.MaximumPage)
            {
                warnings.Add(QueryLimits.PageLimitWarning);
                yield break;
            }

            state.Page++;
            state.Consumed = 0;
            state.ExhaustedSources = ended;
        }
    }

    private void ValidateContinuationSources(PlanNode node, RequestContext context, ExecutionState execution)
    {
        if (node is FetchPlanNode fetch && context.Progress!.Sources.TryGetValue(fetch.Id, out var source))
        {
            var signature = ContinuedSourceSignature(
                fetch,
                context,
                execution.GenreIds,
                execution.SubjectSet.Groups,
                fetch.AnchorKind == QueryAnchorKind.Discover);
            if (source.Signature != signature)
            {
                throw new MetadataSearchContinuationException("TMDb source order or identities changed.");
            }
        }

        foreach (var child in node.Children())
        {
            ValidateContinuationSources(child, context, execution);
        }
    }

    private string ContinuedSourceSignature(
        FetchPlanNode fetch,
        RequestContext context,
        IReadOnlyDictionary<string, int> genreIds,
        IReadOnlyList<ResolvedSubjectGroup> subjects,
        bool discover)
    {
        var terms = discover ? [] : SearchTerms(fetch);
        var sources = discover ? DiscoverSources(fetch, subjects, genreIds) : [];
        var count = discover ? sources.Count : terms.Count;
        return JsonSerializer.Serialize(Enumerable.Range(0, count).Select(index => discover
            ? BuildDiscover(
                fetch,
                context.Language,
                genreIds,
                sources[index].Subjects,
                1,
                sources[index].GenreSelections).Request.CacheKey
            : BuildSearch(fetch, context.Language, 1, terms[index]).CacheKey));
    }

    private bool AdvanceSearchSource(QueryScanState state, int sourceCount)
    {
        if (state.ExhaustedSourceIndexes.Count >= sourceCount)
        {
            state.Exhausted = true;
            return true;
        }

        var wrapped = false;
        for (var checkedSources = 0; checkedSources < sourceCount; checkedSources++)
        {
            state.NextSourceIndex++;
            if (state.NextSourceIndex >= sourceCount)
            {
                state.NextSourceIndex = 0;
                state.NextSourcePage++;
                wrapped = true;
            }

            if (!state.ExhaustedSourceIndexes.Contains(state.NextSourceIndex))
            {
                return wrapped;
            }
        }

        state.Exhausted = true;
        return true;
    }

    private IEnumerable<SearchHit> InterleaveHits(IReadOnlyList<IReadOnlyList<SearchHit>> batches)
    {
        var maximum = batches.Count == 0 ? 0 : batches.Max(batch => batch.Count);
        for (var index = 1; index < maximum; index++)
        {
            foreach (var batch in batches)
            {
                if (index < batch.Count)
                {
                    yield return batch[index];
                }
            }
        }
    }

    private async Task<PageResult> FetchPageAsync(
        ContentRoute route,
        RequestContext context,
        Request request,
        CancellationToken cancellationToken)
    {
        if (route == ContentRoute.Movie)
        {
            var envelope = await client.GetAsync<SearchEnvelopeDto<MovieSearchHitDto>>(
                context,
                request,
                cancellationToken).ConfigureAwait(false);
            var hits = envelope?.Results?.Select((hit, index) => hit.ToSearchHit(index)).ToList() ??
                [];
            return new PageResult(
                hits,
                Math.Max(0, envelope?.TotalPages ?? 0),
                Math.Max(0, envelope?.TotalResults ?? 0));
        }

        var tvEnvelope = await client.GetAsync<SearchEnvelopeDto<TvSearchHitDto>>(
            context,
            request,
            cancellationToken).ConfigureAwait(false);
        var tvHits = tvEnvelope?.Results?.Select((hit, index) => hit.ToSearchHit(index)).ToList() ??
            [];
        return new PageResult(
            tvHits,
            Math.Max(0, tvEnvelope?.TotalPages ?? 0),
            Math.Max(0, tvEnvelope?.TotalResults ?? 0));
    }

    private async Task<CreditResponse> GetCreditsAsync(
        ResolvedSubject subject,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var request = BuildPersonCredits(subject.Id, context.Language);
        var key = request.CacheKey;
        return await cache.GetCreditsAsync(
            key,
            RequestRole.TvCredits,
            (Client: client, Context: context, Request: request),
            static async (state, token) => new CreditResponse
            {
                Tv = await state.Client.GetAsync<PersonTvCreditsDto>(state.Context, state.Request, token)
                    .ConfigureAwait(false)
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<SearchHit?> EnrichAsync(
        FilterPlanNode filter,
        RequestContext context,
        SearchHit hit,
        bool appendCredits,
        CancellationToken cancellationToken)
    {
        var request = BuildDetail(
            filter.Route,
            hit.Id,
            context.Language,
            appendCredits,
            appendExternalIds: NeedsElementDetails(filter, hit, ElementFamily.Reference) ||
                filter.ContentIdFilters.Any(criterion => criterion.Excluded &&
                    criterion.Alternatives.Any(id => !hit.ExternalIds.ContainsKey(id.Source))),
            appendAlternativeTitles: NeedsElementDetails(filter, hit, ElementFamily.AlternateText),
            appendContentRatings: NeedsElementDetails(filter, hit, ElementFamily.ContentRating),
            appendVideos: NeedsElementDetails(filter, hit, ElementFamily.Preview),
            appendImages: NeedsElementDetails(filter, hit, ElementFamily.Image));
        if (filter.Route == ContentRoute.Movie)
        {
            var detail = await client.GetAsync<MovieDetailDto>(context, request, cancellationToken)
                .ConfigureAwait(false);
            return detail is null ? null : MergeHit(hit, detail.FromMovieDetail());
        }

        var tv = await client.GetAsync<TvDetailDto>(context, request, cancellationToken).ConfigureAwait(false);
        return tv is null ? null : MergeHit(hit, tv.FromTvDetail());
    }

    private async Task<SearchHit?> FetchContentIdAsync(
        FetchPlanNode fetch,
        RequestContext context,
        ContentId contentId,
        int sourceOrder,
        bool appendCredits,
        CancellationToken cancellationToken)
    {
        if (contentId.Source.Equals(Constants.ProviderType, StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(contentId.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                return null;
            }

            var appendExternalIds = HasExternalIdExclusions(fetch.ContentIdFilters) ||
                HasElementFamily(fetch.ElementFilters, ElementFamily.Reference);
            var request = BuildDetail(
                fetch.Route,
                id,
                context.Language,
                appendCredits,
                appendExternalIds: appendExternalIds,
                appendAlternativeTitles: HasElementFamily(fetch.ElementFilters, ElementFamily.AlternateText),
                appendContentRatings: HasElementFamily(fetch.ElementFilters, ElementFamily.ContentRating),
                appendVideos: HasElementFamily(fetch.ElementFilters, ElementFamily.Preview),
                appendImages: HasElementFamily(fetch.ElementFilters, ElementFamily.Image));
            if (fetch.Route == ContentRoute.Movie)
            {
                var detail = await client.GetAsync<MovieDetailDto>(context, request, cancellationToken)
                    .ConfigureAwait(false);
                return detail is null
                    ? null
                    : detail.FromMovieDetail() with { SourceOrder = sourceOrder };
            }

            var tv = await client.GetAsync<TvDetailDto>(context, request, cancellationToken).ConfigureAwait(false);
            return tv is null ? null : tv.FromTvDetail() with { SourceOrder = sourceOrder };
        }

        if (!TryMapExternalContentIdSource(contentId.Source, out var externalSource, out var resultSource))
        {
            return null;
        }

        var find = BuildFind(contentId.Value, externalSource, context.Language);
        var envelope = await client.GetAsync<FindEnvelopeDto>(context, find, cancellationToken).ConfigureAwait(false);
        var hit = fetch.Route == ContentRoute.Movie
            ? envelope?.MovieResults?.Select((item, index) => item.ToSearchHit(sourceOrder + index))
                .FirstOrDefault()
            : envelope?.TvResults?.Select((item, index) => item.ToSearchHit(sourceOrder + index))
                .FirstOrDefault();
        if (hit is null)
        {
            return null;
        }

        var externalIds = new Dictionary<string, string>(hit.ExternalIds, StringComparer.OrdinalIgnoreCase)
        {
            [resultSource] = contentId.Value
        };
        return hit with { ExternalIds = externalIds };
    }

    private async Task<IReadOnlyDictionary<string, int>> ResolveGenresAsync(
        ResolvePlanNode resolve,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var request = BuildGenreList(resolve.Route, context.Language);
        return await cache.GetGenreMapAsync(context, request, client, cancellationToken).ConfigureAwait(false);
    }

    private bool SourcePageLimitReached(QueryScanState state, List<string> warnings)
    {
        if (state.NextSourcePage <= QueryLimits.MaximumPage)
        {
            return false;
        }

        state.Exhausted = true;
        if (!warnings.Contains(QueryLimits.PageLimitWarning))
        {
            warnings.Add(QueryLimits.PageLimitWarning);
        }

        return true;
    }

    private QueryExecution LogicalResult(
        IReadOnlyList<SearchHit> hits,
        List<string> warnings,
        int requestedPage)
    {
        return LogicalResult(EnumerateAsync(hits), warnings, requestedPage);
    }

    private QueryExecution LogicalResult(
        IAsyncEnumerable<SearchHit> hits,
        List<string> warnings,
        int requestedPage)
    {
        return new()
        {
            Supported = true,
            Hits = hits,
            Warnings = warnings,
            RequestedPage = requestedPage,
            PagingMode = QueryPagingMode.LogicalStream
        };
    }

    private QueryExecution SourceResult(
        IReadOnlyList<SearchHit> hits,
        List<string> warnings,
        int requestedPage,
        bool hasMore)
    {
        return new()
        {
            Supported = true,
            Hits = EnumerateAsync(hits),
            Warnings = warnings,
            RequestedPage = requestedPage,
            PagingMode = QueryPagingMode.RequestedSourcePage,
            SourceHasMore = hasMore
        };
    }

    private async IAsyncEnumerable<SearchHit> EnumerateAsync(IEnumerable<SearchHit> hits)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        foreach (var hit in hits)
        {
            yield return hit;
        }
    }

    private bool IsFullyApplied(
        FetchPlanNode fetch,
        DiscoverRequest request,
        IReadOnlyList<ResolvedSubjectGroup> subjects)
    {
        return fetch.TextFilters.All(request.AppliedTextFilters.Contains) &&
            fetch.PublishDateFilters.All(request.AppliedPublishDateFilters.Contains) &&
            fetch.DurationFilters.All(request.AppliedDurationFilters.Contains) &&
            fetch.GenreFilters.All(request.AppliedGenreFilters.Contains) &&
            fetch.ElementFilters.All(request.AppliedElementFilters.Contains) &&
            subjects.All(group => request.AppliedSubjectGroups.Contains(
                group,
                ResolvedSubjectGroupComparer.Instance)) &&
            fetch.ContentIdFilters.Count == 0;
    }

    private async Task<SubjectResolutionSet> ResolveAsync(
        ResolvePlanNode resolve,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var groups = new List<ResolvedSubjectGroup>(resolve.SubjectBindings.Count);
        var warnings = new List<string>();
        var requiredUnresolved = false;
        var hadRemoteFailure = false;

        foreach (var binding in resolve.SubjectBindings)
        {
            var filter = binding.Filter;
            var identities = filter.Alternatives.SelectMany((alternative, alternativeIndex) =>
                alternative.IdentityClauses.SelectMany((clause, clauseIndex) =>
                    clause.Select(identity => (
                        AlternativeIndex: alternativeIndex,
                        ClauseIndex: clauseIndex,
                        identity)))).ToList();

            using var concurrency = new SemaphoreSlim(MaximumConcurrency, MaximumConcurrency);
            var tasks = identities.SelectMany(item => filter.Alternatives[item.AlternativeIndex].Roles.Select(
                async role =>
            {
                await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return (
                        item.AlternativeIndex,
                        item.ClauseIndex,
                        Role: role,
                        Results: await ResolveIdentityAsync(
                            role,
                            item.identity,
                            context,
                            cancellationToken).ConfigureAwait(false));
                }
                finally
                {
                    concurrency.Release();
                }
            })).ToList();
            var outcomes = await Task.WhenAll(tasks).ConfigureAwait(false);
            var unsupportedRoles = outcomes
                .GroupBy(outcome => (outcome.AlternativeIndex, outcome.ClauseIndex, outcome.Role))
                .Where(clause => clause.All(outcome => outcome.Results.Count > 0 &&
                    outcome.Results.All(result => result.Kind == SubjectResolutionKind.Unsupported)))
                .Select(clause => (clause.Key.AlternativeIndex, clause.Key.Role))
                .ToHashSet();
            var evaluableOutcomes = outcomes
                .Where(outcome => !unsupportedRoles.Contains((outcome.AlternativeIndex, outcome.Role)) &&
                    outcome.Results.Any(result => result.Kind != SubjectResolutionKind.Unsupported))
                .ToList();
            var evaluableAlternatives = filter.Alternatives.Select((alternative, alternativeIndex) => alternative with
            {
                Roles = [.. alternative.Roles.Where(role => !unsupportedRoles.Contains((alternativeIndex, role)))]
            }).Where(alternative => alternative.Roles.Count > 0).ToList();
            var resolved = new List<ResolvedSubject>();
            for (var alternativeIndex = 0; alternativeIndex < filter.Alternatives.Count; alternativeIndex++)
            {
                var alternative = filter.Alternatives[alternativeIndex];
                foreach (var role in alternative.Roles)
                {
                    if (unsupportedRoles.Contains((alternativeIndex, role)))
                    {
                        continue;
                    }

                    var clauseResults = Enumerable.Range(0, alternative.IdentityClauses.Count)
                        .Select(clauseIndex => outcomes.Where(outcome =>
                            outcome.AlternativeIndex == alternativeIndex &&
                            outcome.ClauseIndex == clauseIndex &&
                            outcome.Role == role)
                            .SelectMany(outcome => outcome.Results)
                            .Where(result => result.Kind == SubjectResolutionKind.Resolved)
                            .GroupBy(result => result.Id)
                            .Select(group => group.First())
                            .ToList())
                        .ToList();
                    if (clauseResults.Count == 0 || clauseResults.Any(clause => clause.Count == 0))
                    {
                        continue;
                    }

                    var commonIds = clauseResults.Skip(1).Aggregate(
                        clauseResults[0].Select(result => result.Id).ToHashSet(),
                        (current, clause) =>
                        {
                            current.IntersectWith(clause.Select(result => result.Id));
                            return current;
                        });
                    foreach (var id in commonIds)
                    {
                        var result = clauseResults.SelectMany(clause => clause)
                            .First(candidate => candidate.Id == id);
                        resolved.Add(new ResolvedSubject(
                            id,
                            result.Name ?? id.ToString(CultureInfo.InvariantCulture),
                            role,
                            result.ViaCompanySearch,
                            ExternalIds(result)));
                    }
                }
            }

            resolved = [.. resolved.GroupBy(subject => (subject.Id, subject.Role)).Select(group => group.First())];

            var incomplete = filter.RequiresLocalVerification ||
                evaluableOutcomes.Any(outcome =>
                    outcome.Results.Count == 0 ||
                    outcome.Results.Any(result => result.Kind != SubjectResolutionKind.Resolved ||
                        result.Incomplete ||
                        result.ViaCompanySearch));
            var incompleteRoles = evaluableOutcomes.Where(outcome => outcome.Results.Count == 0 ||
                outcome.Results.Any(result => result.Kind != SubjectResolutionKind.Resolved ||
                    result.Incomplete || result.ViaCompanySearch))
                .Select(outcome => outcome.Role)
                .ToHashSet();
            if (filter.RequiresLocalVerification)
            {
                incompleteRoles.UnionWith(evaluableAlternatives.SelectMany(alternative => alternative.Roles));
            }

            var remoteFailure = outcomes.Any(outcome => outcome.Results.Any(result =>
                result.Kind == SubjectResolutionKind.Failed));
            hadRemoteFailure |= remoteFailure;
            foreach (var result in outcomes.SelectMany(outcome => outcome.Results)
                .Where(result => result.Kind != SubjectResolutionKind.Unsupported && result.Warning is not null))
            {
                warnings.Add(result.Warning!);
            }

            if (filter.Excluded && unsupportedRoles.Count > 0 ||
                !filter.Excluded && evaluableAlternatives.Count == 0 ||
                resolved.Count == 0 && !filter.Excluded && !filter.RequiresLocalVerification && !remoteFailure)
            {
                requiredUnresolved = true;
            }

            groups.Add(new ResolvedSubjectGroup
            {
                BindingKey = binding.Key,
                Excluded = filter.Excluded,
                Subjects = resolved,
                Alternatives = evaluableAlternatives,
                Incomplete = incomplete,
                IncompleteRoles = incompleteRoles
            });
        }

        return new SubjectResolutionSet
        {
            Groups = groups,
            Warnings = warnings,
            RequiredGroupUnresolved = requiredUnresolved,
            HadRemoteFailure = hadRemoteFailure
        };
    }

    private async Task<IReadOnlyList<SubjectResolution>> ResolveIdentityAsync(
        SubjectRole role,
        SubjectIdentity identity,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        if (TryDirectId(identity, out var direct))
        {
            return [direct];
        }

        if (!string.IsNullOrWhiteSpace(identity.ExternalIdSource) && !string.IsNullOrWhiteSpace(identity.ExternalId))
        {
            if (role is SubjectRole.Company or SubjectRole.Network ||
                !TryExternalSource(identity.ExternalIdSource, out var source))
            {
                return [new SubjectResolution
                {
                    Kind = SubjectResolutionKind.Unsupported
                }];
            }

            var request = BuildFind(identity.ExternalId, source, context.Language);
            var key = "find|" + role + "|" + source + "|" + identity.ExternalId.ToLowerInvariant();
            var external = await cache.GetSubjectResolutionAsync(
                key,
                (
                    Request: request,
                    ExternalIdSource: identity.ExternalIdSource.Trim(),
                    ExternalId: identity.ExternalId,
                    Context: context),
                (state, token) => ResolveExternalAsync(
                    state.Request,
                    state.ExternalIdSource,
                    state.ExternalId,
                    state.Context,
                    token),
                cancellationToken).ConfigureAwait(false);
            return [external];
        }

        if (string.IsNullOrWhiteSpace(identity.Name))
        {
            return [new SubjectResolution { Kind = SubjectResolutionKind.NotFound }];
        }

        var normalized = NormalizeName(identity.Name);
        var nameKey = string.Format(
            CultureInfo.InvariantCulture,
            "name|{0}|{1}|{2}|{3}|{4}",
            RoleNoun(role),
            context.Language.ToLowerInvariant(),
            (int)identity.Operator,
            identity.Negate ? 1 : 0,
            normalized);
        var resolutions = await cache.GetSubjectResolutionsAsync(
            nameKey,
            (Role: role, Identity: identity, Context: context),
            (state, token) => ResolveNameAsync(state.Role, state.Identity, state.Context, token),
            cancellationToken).ConfigureAwait(false);
        return role == SubjectRole.Network
            ? [.. resolutions.Select(resolution => resolution.Kind == SubjectResolutionKind.Resolved
                ? resolution with { ViaCompanySearch = true }
                : resolution)]
            : resolutions;
    }
}
