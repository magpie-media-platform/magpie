using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb.Mapping;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed class QueryEngine(
    ISettingsResolver settingsResolver,
    IQueryExecutor executor,
    IQueryCache cache,
    ITmdbClient client,
    IQueryPlanner planner,
    ILogger<QueryEngine> logger) : IQueryEngine
{
    public async Task<QueryExecution> SearchAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        return await SearchAsync(request, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<QueryExecution> SearchAsync(
        QueryRequest request,
        ExecutionTrace? executionTrace,
        CancellationToken cancellationToken)
    {
        var plan = planner.Plan(request);
        var context = await settingsResolver.AcquireContextAsync(
            plan.DefaultLanguagePreference,
            cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            var unavailable = new QueryExecution
            {
                Supported = true,
                Hits = EmptyAsync(),
                Warnings =
                [
                    "TMDb search is unavailable because the provider is not configured."
                ],
                RequestedPage = plan.RequestedPage,
                PagingMode = QueryPagingMode.LogicalStream
            };
            return unavailable;
        }

        var startedTimestamp = Stopwatch.GetTimestamp();
        executionTrace ??= logger.IsEnabled(LogLevel.Trace) ? new ExecutionTrace() : null;
        if (executionTrace is not null)
        {
            context = context with { ExecutionTrace = executionTrace };
        }

        var result = await executor.ExecuteAsync(plan, context, cancellationToken).ConfigureAwait(false);
        result.Diagnostics.Trace = context.ExecutionTrace;
        result.Diagnostics.Plan = plan;
        result.Diagnostics.StartedTimestamp = startedTimestamp;
        return result with
        {
            Hits = ResolveImagesAsync(
                GuardAsync(
                    result.Hits,
                    plan,
                    result.Warnings,
                    result.Diagnostics,
                    result.SourceHasMore,
                    cancellationToken),
                context,
                cancellationToken)
        };
    }

    private async IAsyncEnumerable<SearchHit> ResolveImagesAsync(
        IAsyncEnumerable<SearchHit> hits,
        RequestContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var imageBase = default(string);
        await foreach (var hit in hits.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (hit.PosterPath is null)
            {
                yield return hit;
                continue;
            }

            imageBase ??= await ResolveImageBaseAsync(
                context with { PlanNodeId = hit.SourcePlanNodeId ?? context.PlanNodeId },
                cancellationToken).ConfigureAwait(false);
            yield return hit with
            {
                ImageUrl = hit.PosterPath.ResolveImageUrl(imageBase, "w92")
            };
        }
    }

    private async Task<string> ResolveImageBaseAsync(RequestContext context, CancellationToken cancellationToken)
    {
        return await cache.GetImageBaseUrlAsync(context, client, logger, cancellationToken).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<SearchHit> GuardAsync(
        IAsyncEnumerable<SearchHit> hits,
        QueryPlan plan,
        List<string> warnings,
        QueryExecutionDiagnostics diagnostics,
        bool sourceHasMore,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var enumerator = hits.GetAsyncEnumerator(cancellationToken);
        var surviving = 0;
        try
        {
            while (true)
            {
                var hasNext = false;
                var failed = false;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "TMDb query execution failed for plan root {PlanRoot}",
                        plan.Root.GetType().Name);
                    var warning = "TMDb could not complete the remote query.";
                    if (!warnings.Contains(warning))
                    {
                        warnings.Add(warning);
                    }

                    diagnostics.StreamFaulted = true;
                    failed = true;
                }

                if (failed || !hasNext)
                {
                    yield break;
                }

                surviving++;
                yield return enumerator.Current;
            }
        }
        finally
        {
            if (diagnostics.Trace is { } trace && diagnostics.Plan is { } tracedPlan)
            {
                var hasMore = sourceHasMore ||
                    diagnostics.StreamFaulted ||
                    (tracedPlan.MaximumResults is { } maximumResults && surviving > maximumResults);
                trace.CompleteExecution(hasMore);
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    var executionJson = trace.SerializeExecution(
                        TimeSpan.FromSeconds(
                            (Stopwatch.GetTimestamp() - diagnostics.StartedTimestamp) / (double)Stopwatch.Frequency),
                        surviving,
                        hasMore);
                    logger.LogTrace("TMDb query execution {Execution}", executionJson);
                }
            }
        }
    }

    private async IAsyncEnumerable<SearchHit> EmptyAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}
