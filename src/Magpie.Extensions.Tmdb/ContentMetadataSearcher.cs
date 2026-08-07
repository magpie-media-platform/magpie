using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Extensions.Tmdb.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tmdb;

internal sealed class ContentMetadataSearcher(
    IQueryEngine queryEngine,
    ISettingsResolver settingsResolver,
    ITmdbClient client,
    IHydrationCache hydrationCache,
    IQueryCache queryCache,
    ILogger<ContentMetadataSearcher> logger)
    : IContentHierarchyMetadataAdvancedSearcher
{
    private static readonly string UnsupportedTvLevelWarning =
        "TMDb advanced search cannot search individual TV seasons or episodes; those matching levels were " +
            "omitted.";

    public string ProviderType => Tmdb.Constants.ProviderType;

    public IEnumerable<string> SupportedContentTypes => ["movie", "tv"];

    public async Task<IEnumerable<MetadataSearchCandidate>> SearchAsync(
        ContentHierarchyLevelDefinition levelDefinition,
        string searchTerm,
        string defaultLanguagePreference,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return [];
        }

        var route = levelDefinition.Slug.ResolveDiscoverRoute();
        if (route is null)
        {
            logger.LogDebug("TMDb searcher does not support level slug {Slug}", levelDefinition.Slug);
            return [];
        }

        var execution = await queryEngine.SearchAsync(
            new QueryRequest
            {
                Route = route.Value,
                Criteria = new MetadataCriteria(),
                Page = Math.Max(1, page),
                DefaultLanguagePreference = defaultLanguagePreference,
                OrdinarySearchTerm = searchTerm.Trim()
            },
            cancellationToken).ConfigureAwait(false);
        var result = await MaterializeAsync(execution, cancellationToken).ConfigureAwait(false);
        return [.. result.Hits.Select(hit => hit.ToMetadataSearchCandidate())];
    }

    public async Task<MetadataSearchPage> AdvancedSearchAsync(
        string contentType,
        ContentHierarchyItemCriteria query,
        int maxResults,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        return await AdvancedSearchAsync(
            contentType,
            query,
            maxResults,
            defaultLanguagePreference,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<MetadataSearchPage> AdvancedSearchAsync(
        string contentType,
        ContentHierarchyItemCriteria query,
        int maxResults,
        string defaultLanguagePreference,
        ExecutionTrace? executionTrace,
        CancellationToken cancellationToken)
    {
        if (!new[] { "movie", "tv" }.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            logger.LogDebug(
                "TMDb provider does not support advanced search for content type {ContentType}",
                contentType);
            return MetadataSearchPage.NotSupported;
        }

        var route = contentType.ResolveAdvancedSearchRoute(query, out var unsupportedTvLevelsSelected);
        string[] scopeWarnings = unsupportedTvLevelsSelected
            ? [UnsupportedTvLevelWarning]
            : [];
        if (route is null)
        {
            return EmptySearchPage(scopeWarnings);
        }

        var result = await SearchMaximumAsync(
            route.Value,
            query.MergeMetadata(),
            Math.Max(1, maxResults),
            defaultLanguagePreference,
            executionTrace,
            cancellationToken).ConfigureAwait(false);
        return new MetadataSearchPage
        {
            Supported = result.Supported,
            Matches = [.. result.Hits.Select(hit => hit.ToMetadataSearchCandidate())],
            Warnings = [.. scopeWarnings.Concat(result.Warnings).Distinct()],
            PageInfo = result.PageInfo
        };
    }

    public async Task<MetadataSearchCandidate?> HydrateAsync(
        MetadataSearchCandidate candidate,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        if (candidate.ProviderType != ProviderType ||
            !int.TryParse(candidate.ExternalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
            id <= 0)
        {
            return null;
        }

        var route = candidate.ContentType switch
        {
            "movie" => ContentRoute.Movie,
            "tv" => ContentRoute.TvShow,
            _ => (ContentRoute?)null
        };
        if (route is null)
        {
            return null;
        }

        var context = await settingsResolver.AcquireContextAsync(defaultLanguagePreference, cancellationToken)
            .ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        var key = HydrationCache.HydrationKey.Create(
            candidate.ProviderType,
            candidate.ContentType,
            id,
            context.Language);
        var tmdbRequest = BuildHydrationRequest(route.Value, id, context.Language);
        var hydrated = await hydrationCache.GetOrCreateAsync(
            key,
            (
                Route: route.Value,
                Context: context,
                Request: tmdbRequest,
                Client: client,
                QueryCache: queryCache,
                Logger: logger),
            static async (state, token) =>
            {
                var hit = default(SearchHit);
                if (state.Route == ContentRoute.Movie)
                {
                    var detail = await state.Client.GetAsync<MovieDetailDto>(state.Context, state.Request, token)
                        .ConfigureAwait(false);
                    hit = detail?.FromMovieDetail();
                }
                else
                {
                    var detail = await state.Client.GetAsync<TvDetailDto>(state.Context, state.Request, token)
                        .ConfigureAwait(false);
                    hit = detail?.FromTvDetail();
                }

                if (hit is null)
                {
                    return null;
                }

                if (hit.PosterPath is not null)
                {
                    var imageBase = await state.QueryCache.GetImageBaseUrlAsync(
                        state.Context,
                        state.Client,
                        state.Logger,
                        token).ConfigureAwait(false);
                    hit = hit with { ImageUrl = hit.PosterPath.ResolveImageUrl(imageBase, "w92") };
                }

                return hit.ToMetadataSearchCandidate();
            },
            cancellationToken).ConfigureAwait(false);
        if (hydrated is null)
        {
            return null;
        }

        return hydrated with
        {
            SubjectReferences = MergeSubjectReferences(hydrated.SubjectReferences, candidate.SubjectReferences)
        };
    }

    private static IReadOnlyList<ProviderMetadataSearchSubjectReference> MergeSubjectReferences(
        IReadOnlyList<ProviderMetadataSearchSubjectReference> hydrated,
        IReadOnlyList<ProviderMetadataSearchSubjectReference> candidate)
    {
        var merged = hydrated.ToList();
        foreach (var reference in candidate)
        {
            var index = merged.FindIndex(value => SameRelationshipSubject(value, reference));
            if (index < 0)
            {
                merged.Add(reference);
                continue;
            }

            var externalIds = merged[index].ExternalIds.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            foreach (var pair in reference.ExternalIds)
            {
                externalIds[pair.Key] = pair.Value;
            }

            merged[index] = merged[index] with { ExternalIds = externalIds };
        }

        return merged;
    }

    private static bool SameRelationshipSubject(
        ProviderMetadataSearchSubjectReference left,
        ProviderMetadataSearchSubjectReference right)
    {
        return string.Equals(left.RelationshipKind, right.RelationshipKind, StringComparison.OrdinalIgnoreCase) &&
        (left.ExternalIds.Any(pair => right.ExternalIds.TryGetValue(pair.Key, out var value) &&
            string.Equals(pair.Value, value, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(left.SubjectKind, right.SubjectKind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
    }

    private static Request BuildHydrationRequest(
        ContentRoute route,
        int id,
        string language)
    {
        var appends = route == ContentRoute.Movie
            ? "release_dates,credits,images,external_ids,videos"
            : "content_ratings,credits,images,external_ids,videos";
        var query = new List<(string Key, string Value)>
        {
            ("language", language),
            ("append_to_response", appends)
        };

        var path = string.Format(
            CultureInfo.InvariantCulture,
            route == ContentRoute.Movie ? "3/movie/{0}" : "3/tv/{0}",
            id);
        return new Request
        {
            Endpoint = "hydration-detail",
            Path = path,
            QueryParameters = query,
            CacheKey = path + "?" + string.Join("&", query.Select(pair => pair.Key + "=" + pair.Value)),
            ResponseProfile = ResponseProfile.Detail
        };
    }

    private static MetadataSearchPage EmptySearchPage(
        IReadOnlyList<string> warnings)
    {
        return new()
        {
            Supported = true,
            Matches = [],
            Warnings = warnings,
            PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
        };
    }

    private static async Task<QueryResult> MaterializeAsync(
        QueryExecution execution,
        CancellationToken cancellationToken)
    {
        const int pageSize = 20;
        var hits = new List<SearchHit>();
        var remainingSkip = execution.PagingMode == QueryPagingMode.LogicalStream
            ? ((long)Math.Max(1, execution.RequestedPage) - 1) * pageSize
            : 0;
        await foreach (var hit in execution.Hits.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (remainingSkip > 0)
            {
                remainingSkip--;
                continue;
            }

            hits.Add(hit);
            if (execution.PagingMode == QueryPagingMode.LogicalStream && hits.Count > pageSize)
            {
                break;
            }
        }

        var hasMore = execution.PagingMode == QueryPagingMode.RequestedSourcePage
            ? execution.SourceHasMore
            : execution.Diagnostics.StreamFaulted || hits.Count > pageSize;
        if (hits.Count > pageSize)
        {
            hits.RemoveAt(pageSize);
        }

        return new QueryResult
        {
            Supported = execution.Supported,
            Hits = hits,
            Warnings = [.. execution.Warnings.Distinct()],
            PageInfo = new ProviderSearchPageInfo
            {
                RequestedPage = Math.Max(1, execution.RequestedPage),
                HasMore = hasMore
            }
        };
    }

    private async Task<QueryResult> SearchMaximumAsync(
        ContentRoute route,
        MetadataCriteria criteria,
        int maximumResults,
        string defaultLanguagePreference,
        ExecutionTrace? executionTrace,
        CancellationToken cancellationToken)
    {
        var hits = new List<SearchHit>();
        var warnings = new List<string>();
        var supported = true;
        var hasMore = false;
        var sourcePage = 1;

        while (hits.Count <= maximumResults)
        {
            var execution = await queryEngine.SearchAsync(
                new QueryRequest
                {
                    Route = route,
                    Criteria = criteria,
                    Page = sourcePage,
                    MaximumResults = maximumResults,
                    DefaultLanguagePreference = defaultLanguagePreference
                },
                executionTrace,
                cancellationToken).ConfigureAwait(false);
            supported &= execution.Supported;

            await foreach (var hit in execution.Hits.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                hits.Add(hit);
                if (hits.Count > maximumResults)
                {
                    break;
                }
            }

            warnings.AddRange(execution.Warnings);

            if (!execution.Supported)
            {
                break;
            }

            if (execution.PagingMode == QueryPagingMode.LogicalStream)
            {
                hasMore = execution.Diagnostics.StreamFaulted || hits.Count > maximumResults;
                break;
            }

            hasMore = execution.Diagnostics.StreamFaulted ||
                execution.SourceHasMore ||
                hits.Count > maximumResults;
            if (hits.Count > maximumResults || !execution.SourceHasMore)
            {
                break;
            }

            if (sourcePage >= QueryLimits.MaximumPage)
            {
                if (!warnings.Contains(QueryLimits.PageLimitWarning))
                {
                    warnings.Add(QueryLimits.PageLimitWarning);
                }

                break;
            }

            sourcePage++;
        }

        if (hits.Count > maximumResults)
        {
            hits.RemoveRange(maximumResults, hits.Count - maximumResults);
        }

        return new QueryResult
        {
            Supported = supported,
            Hits = hits,
            Warnings = [.. warnings.Distinct()],
            PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = hasMore }
        };
    }
}
