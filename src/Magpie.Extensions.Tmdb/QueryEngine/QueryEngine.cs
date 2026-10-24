using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed class QueryEngine(
    ISettingsResolver settingsResolver,
    IQueryExecutor executor,
    IQueryCoordinator cache,
    ITmdbClient client,
    IQueryPlanner planner,
    ILogger<QueryEngine> logger) : IQueryEngine
{
    public async Task<QueryExecution> SearchAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        var plan = planner.Plan(request);
        if (plan.IsKnownEmpty() && plan.Warnings.Count == 0)
        {
            return new QueryExecution
            {
                Supported = true,
                Hits = EmptyAsync(),
                Warnings = [],
                RequestedPage = plan.RequestedPage,
                PagingMode = QueryPagingMode.LogicalStream
            };
        }

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

        if (request.Progress is { } progress)
        {
            var signature = plan.SerializePlan();
            if (progress.PlanSignature is not null && progress.PlanSignature != signature)
            {
                throw new MetadataSearchContinuationException("The TMDb query plan changed.");
            }

            progress.PlanSignature = signature;
            var configuration = context.ApiKey + ":" + context.Language;
            if (progress.Configuration is not null && progress.Configuration != configuration)
            {
                throw new MetadataSearchContinuationException("TMDb configuration changed.");
            }

            progress.Configuration = configuration;
            context = context with { Progress = progress, ValidateOnly = request.ValidateOnly };
        }

        var startedTimestamp = Stopwatch.GetTimestamp();
        var activity = Telemetry.Source.StartActivity("tmdb.query.execute");
        try
        {
            activity?.SetTag("magpie.query.fingerprint", plan.Fingerprint);
            if (activity?.IsAllDataRequested == true)
            {
                activity.AddEvent(new ActivityEvent(
                    "plan",
                    tags: new ActivityTagsCollection
                    {
                        ["magpie.query.plan_json"] = plan.SerializePlan(),
                        ["magpie.query.anchor_kind"] = plan.AnchorKindName(),
                        ["magpie.query.known_empty"] = plan.IsKnownEmpty(),
                        ["magpie.query.warnings"] = plan.Warnings.ToArray()
                    }));
            }

            var telemetry = new QueryTelemetry(activity);
            context = context with { Telemetry = telemetry };
            var result = await executor.ExecuteAsync(plan, context, cancellationToken).ConfigureAwait(false);
            return result with
            {
                Hits = ResolveImagesAsync(
                    GuardAsync(
                        result.Hits,
                        plan,
                        request.Route,
                        result.Warnings,
                        result.Diagnostics,
                        telemetry,
                        startedTimestamp,
                        result.SourceHasMore,
                        cancellationToken),
                    context,
                    cancellationToken)
            };
        }
        catch (Exception ex)
        {
            if (activity is not null)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.AddException(ex);
                activity.SetTag("error.type", ex.GetType().FullName);
                activity.Dispose();
            }

            var elapsed = (Stopwatch.GetTimestamp() - startedTimestamp) * 1000d / Stopwatch.Frequency;
            Telemetry.Duration.Record(
                elapsed,
                Telemetry.ProviderTag,
                new KeyValuePair<string, object?>("content_type", request.Route.ContentTypeName()),
                new KeyValuePair<string, object?>(
                    "terminal_reason",
                    cancellationToken.IsCancellationRequested ? TerminalReason.Cancelled : TerminalReason.Error));
            throw;
        }
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

            imageBase ??= await cache.GetImageBaseUrlAsync(
                context with { PlanNodeId = hit.SourcePlanNodeId ?? context.PlanNodeId },
                client,
                logger,
                cancellationToken).ConfigureAwait(false);
            yield return hit with
            {
                ImageUrl = hit.PosterPath.ResolveImageUrl(imageBase, "w92")
            };
        }
    }

    private async IAsyncEnumerable<SearchHit> GuardAsync(
        IAsyncEnumerable<SearchHit> hits,
        QueryPlan plan,
        ContentRoute route,
        List<string> warnings,
        QueryExecutionDiagnostics diagnostics,
        QueryTelemetry telemetry,
        long startedTimestamp,
        bool sourceHasMore,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerator = hits.GetAsyncEnumerator(cancellationToken);
        await using var _ = enumerator.ConfigureAwait(false);
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
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (MetadataSearchContinuationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (!diagnostics.ReceivedResponse)
                    {
                        throw;
                    }

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
                    if (!diagnostics.ReceivedResponse && diagnostics.StreamFaulted)
                    {
                        throw new HttpRequestException("TMDb could not complete the remote query.");
                    }

                    yield break;
                }

                surviving++;
                diagnostics.ReceivedResponse = true;
                yield return enumerator.Current;
            }
        }
        finally
        {
            var hasMore = sourceHasMore ||
                diagnostics.StreamFaulted ||
                (plan.MaximumResults is { } maximumResults && surviving > maximumResults);
            var terminalReason = diagnostics.StreamFaulted
                ? TerminalReason.Error
                : cancellationToken.IsCancellationRequested
                    ? TerminalReason.Cancelled
                    : plan.IsKnownEmpty()
                        ? TerminalReason.KnownEmpty
                        : hasMore
                            ? TerminalReason.Unbounded
                            : TerminalReason.Completed;
            var elapsed = (Stopwatch.GetTimestamp() - startedTimestamp) * 1000d / Stopwatch.Frequency;
            var contentType = new KeyValuePair<string, object?>("content_type", route.ContentTypeName());
            Telemetry.Duration.Record(
                elapsed,
                Telemetry.ProviderTag,
                contentType,
                new KeyValuePair<string, object?>("terminal_reason", terminalReason));
            Telemetry.Results.Record(surviving, Telemetry.ProviderTag, contentType);
            if (telemetry.Activity is { } activity)
            {
                activity.SetTag("magpie.query.rows_fetched", telemetry.RowsFetched);
                activity.SetTag("magpie.query.rows_surviving", surviving);
                activity.SetTag("magpie.query.request_count", telemetry.Requests);
                activity.SetTag("magpie.query.pruned_nodes", telemetry.PrunedNodes);
                activity.SetTag("magpie.query.has_more", hasMore);
                activity.SetTag("magpie.query.terminal_reason", terminalReason);
                if (diagnostics.StreamFaulted)
                {
                    activity.SetStatus(ActivityStatusCode.Error);
                }

                activity.Dispose();
            }
        }
    }

    private async IAsyncEnumerable<SearchHit> EmptyAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}
