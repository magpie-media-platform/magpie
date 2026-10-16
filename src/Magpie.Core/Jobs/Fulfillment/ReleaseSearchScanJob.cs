using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Fulfillment;

internal sealed class ReleaseSearchScanJob(
    IJobManager jobManager,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    TimeProvider timeProvider,
    ILogger<ReleaseSearchScanJob> logger,
    IProgressReporter progressReporter) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.ReleaseSearchScan,
        DisplayName = "Release search scan",
        Description = "Queues release searches for monitored content that is missing or below the quality cutoff.",
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
                MagpieJobs.Parameters.LeadTimeDays,
                "Days before its publish date that the scan starts to search content. Content with no publish " +
                "date is always searched.",
                DataType.Number,
                false,
                DefaultLeadTimeDays.ToString(CultureInfo.InvariantCulture))
        ],
    };

    private const int PageSize = 500;

    private const int DefaultLeadTimeDays = 1;

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        var userId = jobContext.ManualTriggerUserId;
        var correlationId = jobContext.CorrelationId;
        var leadTimeDays = ReadIntParameter(
            jobContext.Parameters,
            MagpieJobs.Parameters.LeadTimeDays,
            DefaultLeadTimeDays,
            minimum: 0);

        progressReporter.Report(0, "The scan will select the monitored hierarchy roots.");

        var publishedBy = timeProvider.GetUtcNow().UtcDateTime.AddDays(leadTimeDays);
        var roots = await GetSubtreeRootsAsync(publishedBy, cancellationToken).ConfigureAwait(false);

        var totalItems = roots.Count;
        var completedItems = 0;
        foreach (var rootId in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progressReporter.ReportFraction(completedItems, totalItems);

            try
            {
                await EnqueueReleaseSearchAsync(rootId, leadTimeDays, userId, correlationId, cancellationToken)
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

    private async Task<IReadOnlyList<Guid>> GetSubtreeRootsAsync(
        DateTime publishedBy,
        CancellationToken cancellationToken)
    {
        var needyLeafIds = await LoadUnfulfilledMonitoredLeafIdsAsync(publishedBy, cancellationToken)
            .ConfigureAwait(false);
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

    private async Task<List<Guid>> LoadUnfulfilledMonitoredLeafIdsAsync(
        DateTime publishedBy,
        CancellationToken cancellationToken)
    {
        var leafIds = new List<Guid>();
        Guid? afterId = null;
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
                    Metadata = new()
                    {
                        And = [new()
                        {
                            PublishDate = new()
                            {
                                And = [new()
                                {
                                    Operator = CriteriaValueOperator.GreaterThan,
                                    Value = new TemporalValue
                                    {
                                        Value = publishedBy,
                                        Precision = TemporalPrecision.Instant,
                                    },
                                    Negate = true,
                                }],
                            },
                        }],
                    },
                    AfterId = afterId,
                },
                new PagingParameter { PageNumber = 1, PageSize = PageSize },
                new() { PropertyName = nameof(ContentHierarchyItem.Id), Direction = SortDirection.Ascending },
                cancellationToken).ConfigureAwait(false);

            leafIds.AddRange(page);
            if (page.Count < PageSize)
            {
                break;
            }

            afterId = page[^1];
        }

        return leafIds;
    }

    private async Task EnqueueReleaseSearchAsync(
        Guid contentHierarchyItemId,
        int leadTimeDays,
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
                    [MagpieJobs.Parameters.LeadTimeDays] = leadTimeDays.ToString(CultureInfo.InvariantCulture),
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

    private int ReadIntParameter(
        IReadOnlyDictionary<string, string> parameters,
        string parameterName,
        int defaultValue,
        int minimum)
    {
        if (!parameters.TryGetValue(parameterName, out var raw))
        {
            return defaultValue;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured) &&
            configured >= minimum)
        {
            return configured;
        }

        logger.LogWarning(
            "Parameter {Parameter} value {Raw} is not an integer of at least {Minimum}, so the job will use the " +
            "default {Default}",
            parameterName,
            raw,
            minimum,
            defaultValue);

        return defaultValue;
    }
}
