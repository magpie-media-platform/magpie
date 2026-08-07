using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    TimeProvider timeProvider,
    ILogger<ReleaseSearchScanJob> logger,
    IProgressReporter? progressReporter = null) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = "ReleaseSearchScan",
        DisplayName = "Release search scan",
        Description = "Enqueues a recursive release search at the root of every monitored subtree.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Requeue,
        Timeout = TimeSpan.FromMinutes(15),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters =
        [
            new(
                BackpressureThresholdParameter,
                "Maximum queued or running downstream ReleaseSearchItem executions before the sweep skips.",
                DataType.Number,
                false,
                DefaultBackpressureThreshold.ToString(CultureInfo.InvariantCulture))
        ],
    };

    private const int PageSize = 500;

    private const int DefaultBackpressureThreshold = 50;
    private const string BackpressureThresholdParameter = "backpressureThreshold";

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        var userId = jobContext.ManualTriggerUserId;
        var correlationId = jobContext.CorrelationId;
        var progress = progressReporter;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var backpressureThreshold = ReadPositiveIntParameter(
            jobContext.Parameters,
            BackpressureThresholdParameter,
            DefaultBackpressureThreshold);

        var activeReleaseSearches = (await jobExecutionRepository
            .QueryAsync(
                new JobExecutionQuery
                {
                    JobType = MagpieJobs.Types.ReleaseSearchItem,
                    Timestamp = new Range<DateTime>(now.AddHours(-1), null),
                    Statuses = [JobExecutionStatus.Queued, JobExecutionStatus.Running],
                },
                new()
                {
                    PageNumber = 1,
                    PageSize = backpressureThreshold == int.MaxValue ? int.MaxValue : backpressureThreshold + 1,
                },
                sort: null,
                cancellationToken)
            .ConfigureAwait(false)).Items.ToList();
        if (activeReleaseSearches.Count > backpressureThreshold)
        {
            logger.LogWarning(
                "ReleaseSearchItem has {ActiveCount} queued or running executions (threshold {Threshold})." +
                " The release search scan skipped this run",
                activeReleaseSearches.Count,
                backpressureThreshold);
            progress?.Report(1, "The scan stopped because the ReleaseSearchItem queue is not empty.");
            return;
        }

        progress?.Report(0, "The scan will select the monitored hierarchy roots.");

        var roots = await GetSubtreeRootsAsync(cancellationToken).ConfigureAwait(false);

        var totalItems = roots.Count;
        var completedItems = 0;
        foreach (var rootId in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.ReportFraction(completedItems, totalItems);

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

        progress?.Report(1, "The release-search scan completed.");
    }

    private async Task<IReadOnlyList<Guid>> GetSubtreeRootsAsync(CancellationToken cancellationToken)
    {
        var roots = new List<Guid>();
        var pageNumber = 1;
        while (true)
        {
            var page = await contentHierarchyItemRepository.QueryAsync(
                new ContentHierarchyItemCriteria
                {
                    EffectiveMonitoring = true,
                    HasMonitoredParent = false,
                },
                new Models.Paging.PagingParameter { PageNumber = pageNumber, PageSize = PageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);

            foreach (var item in page.Items)
            {
                roots.Add(item.Id);
            }

            if (pageNumber * PageSize >= page.TotalItems || !page.Items.Any())
            {
                break;
            }

            pageNumber++;
        }

        return roots;
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

        logger.LogInformation(
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
            "Parameter {Parameter} value {Raw} is not a positive integer. The job will use the default {Default}",
            parameterName,
            raw,
            defaultValue);

        return defaultValue;
    }
}
