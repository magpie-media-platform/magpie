using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary.Mapping;
using Magpie.Models.MetadataSearch;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed class SearchEngine(
    IQueryPlanner planner,
    IQueryExecutor executor,
    ILogger<SearchEngine> logger) : ISearchEngine
{
    public async Task<QueryResult> SearchAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        var plan = planner.Plan(request) with { Progress = request.Progress };
        try
        {
            return await executor.ExecuteSearchAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (request.Intent == QueryIntent.AdvancedSearch &&
            ex is HttpRequestException or TimeoutException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Open Library advanced search failed");
            return new QueryResult
            {
                Hits = [],
                PageInfo = new ProviderSearchPageInfo
                {
                    RequestedPage = Math.Max(1, plan.Page),
                    HasMore = true
                },
                Warnings = ["Open Library could not complete the remote search."],
                CandidateLanguagePreferences = plan.CandidateLanguagePreferences
            };
        }
    }

    public Task<WorkResolution> ResolveWorkAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        return executor.ResolveWorkAsync(planner.Plan(request), cancellationToken);
    }

    public Task<AuthorDto?> GetAuthorAsync(string authorOlid, CancellationToken cancellationToken)
    {
        return executor.GetAuthorAsync(authorOlid, cancellationToken);
    }

    public Task<EditionDto?> GetRepresentativeEditionAsync(WorkHit hit, CancellationToken cancellationToken)
    {
        return executor.GetRepresentativeEditionAsync(hit, cancellationToken);
    }
}
