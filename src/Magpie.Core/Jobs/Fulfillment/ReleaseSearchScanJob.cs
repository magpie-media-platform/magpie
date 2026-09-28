using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Fulfillment;

internal sealed class ReleaseSearchScanJob(
    IJobManager jobManager,
    IJobExecutionRepository jobExecutionRepository,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    ILogger<ReleaseSearchScanJob> logger,
    IProgressReporter progressReporter) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.ReleaseSearchScan,
        DisplayName = "Release search scan",
        Description = "Enqueues a recursive release search at the root of every monitored subtree that still " +
            "has a missing or below-cutoff item.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Requeue,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters =
        [
            new(
                MagpieJobs.Parameters.BackpressureThreshold,
                "Maximum queued or running downstream ReleaseSearchItem executions before the sweep skips.",
                DataType.Number,
                false,
                DefaultBackpressureThreshold.ToString(CultureInfo.InvariantCulture))
        ],
    };

    private const int PageSize = 500;

    private const int DefaultBackpressureThreshold = 50;

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        var userId = jobContext.ManualTriggerUserId;
        var correlationId = jobContext.CorrelationId;
        var backpressureThreshold = ReadPositiveIntParameter(
            jobContext.Parameters,
            MagpieJobs.Parameters.BackpressureThreshold,
            DefaultBackpressureThreshold);

        var activeReleaseSearches = await jobExecutionRepository
            .QueryAsync(
                new JobExecutionQuery
                {
                    JobType = MagpieJobs.Types.ReleaseSearchItem,
                    Statuses = [JobExecutionStatus.Queued, JobExecutionStatus.Running],
                },
                new()
                {
                    PageNumber = 1,
                    PageSize = 1,
                },
                sort: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (activeReleaseSearches.TotalItems > backpressureThreshold)
        {
            logger.LogInformation(
                "ReleaseSearchItem has {ActiveCount} queued or running executions (threshold {Threshold})," +
                " so the release search scan skipped this run",
                activeReleaseSearches.TotalItems,
                backpressureThreshold);
            progressReporter.Report(
                1,
                "The scan stopped because the ReleaseSearchItem queue is above its backpressure threshold.");
            return;
        }

        progressReporter.Report(0, "The scan will select the monitored hierarchy roots.");

        var roots = await GetSubtreeRootsAsync(cancellationToken).ConfigureAwait(false);

        var totalItems = roots.Count;
        var completedItems = 0;
        foreach (var rootId in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progressReporter.ReportFraction(completedItems, totalItems);

            try
            {
                await EnqueueReleaseSearchAsync(rootId, userId, correlationId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The release search scan failed to enqueue a search for content hierarchy item " +
                    "{ContentHierarchyItemId}",
                    rootId);
            }

            completedItems++;
        }

        progressReporter.Report(1, "The release-search scan completed.");
    }

    private async Task<IReadOnlyList<Guid>> GetSubtreeRootsAsync(CancellationToken cancellationToken)
    {
        var needyLeafIds = await LoadUnfulfilledMonitoredLeafIdsAsync(cancellationToken).ConfigureAwait(false);
        if (needyLeafIds.Count == 0)
        {
            return [];
        }

        var ancestry = await contentHierarchyItemRepository
            .GetAncestryAsync(needyLeafIds, cancellationToken)
            .ConfigureAwait(false);

        var roots = new HashSet<Guid>();
        foreach (var leafId in needyLeafIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var walked = new HashSet<Guid>();
            var currentId = leafId;
            while (walked.Add(currentId) &&
                ancestry.TryGetValue(currentId, out var node) &&
                node.EffectiveMonitoring != MonitoringMode.Unmonitored)
            {
                if (node.ParentId is not { } parentId ||
                    !ancestry.TryGetValue(parentId, out var parent) ||
                    parent.EffectiveMonitoring == MonitoringMode.Unmonitored)
                {
                    roots.Add(currentId);
                    break;
                }

                currentId = parentId;
            }
        }

        return [.. roots];
    }

    private async Task<List<Guid>> LoadUnfulfilledMonitoredLeafIdsAsync(CancellationToken cancellationToken)
    {
        var leafIds = new List<Guid>();
        var pageNumber = 1;
        while (true)
        {
            var page = await contentHierarchyItemRepository.QueryIdsAsync(
                new ContentHierarchyItemCriteria
                {
                    LeafOnly = true,
                    EffectiveMonitoring = true,
                    ImportPending = false,
                    Fulfillment = new()
                    {
                        And = [new()
                        {
                            Operator = CriteriaValueOperator.In,
                            Values = [FulfillmentStatus.Missing, FulfillmentStatus.Upgradable],
                        }],
                    },
                },
                new Models.Paging.PagingParameter { PageNumber = pageNumber, PageSize = PageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);

            leafIds.AddRange(page);
            if (page.Count < PageSize)
            {
                break;
            }

            pageNumber++;
        }

        return leafIds;
    }

    private async Task EnqueueReleaseSearchAsync(
        Guid contentHierarchyItemId,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        await jobManager.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = MagpieJobs.Types.ReleaseSearchItem,
                Parameters = new Dictionary<string, string>
                {
                    [MagpieJobs.Parameters.ContentHierarchyItemId] = contentHierarchyItemId.ToString("D"),
                    [MagpieJobs.Parameters.SearchKind] = MagpieJobs.ParameterValues.NodeSearchKind,
                },
                Mode = JobExecutionMode.Queue,
                UserId = userId,
                CorrelationId = correlationId,
            },
            cancellationToken).ConfigureAwait(false);

        logger.LogDebug(
            "The release search scan enqueued a monitored search for content hierarchy item {ContentHierarchyItemId}",
            contentHierarchyItemId);
    }

    private int ReadPositiveIntParameter(
        IReadOnlyDictionary<string, string> parameters,
        string parameterName,
        int defaultValue)
    {
        if (!parameters.TryGetValue(parameterName, out var raw))
        {
            return defaultValue;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured) && configured > 0)
        {
            return configured;
        }

        logger.LogWarning(
            "Parameter {Parameter} value {Raw} is not a positive integer, so the job will use the default {Default}",
            parameterName,
            raw,
            defaultValue);

        return defaultValue;
    }
}
