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
using Magpie.Models.Filtering;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Fulfillment;

internal sealed class ReleaseAutoGrabScanJob(
    IJobManager jobManager,
    IJobExecutionRepository jobExecutionRepository,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IContentRepository contentRepository,
    TimeProvider timeProvider,
    ILogger<ReleaseAutoGrabScanJob> logger,
    IProgressReporter? progressReporter = null) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = "ReleaseAutoGrabScan",
        DisplayName = "Release auto-grab scan",
        Description = "Queues an automatic release grab for every monitored item that is ready to download.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Requeue,
        Timeout = TimeSpan.FromMinutes(10),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters =
        [
            new(
                MagpieJobs.Parameters.RetryCooldownHours,
                "Minimum hours before a content item is considered for auto-grab again.",
                DataType.Number,
                false,
                MagpieJobs.ParameterValues.DefaultRetryCooldownHours.ToString(CultureInfo.InvariantCulture)),
            new(
                BackpressureThresholdParameter,
                "Maximum queued or running downstream ReleaseDownloadItem executions before the sweep skips.",
                DataType.Number,
                false,
                DefaultBackpressureThreshold.ToString(CultureInfo.InvariantCulture))
        ],
    };

    private const int PageSize = 500;

    private const int DefaultBackpressureThreshold = 50;
    private const string BackpressureThresholdParameter = "backpressureThreshold";

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        var userId = context.ManualTriggerUserId;
        var correlationId = context.CorrelationId;
        var progress = progressReporter;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var retryCooldownHours = ReadPositiveIntParameter(
            context.Parameters,
            MagpieJobs.Parameters.RetryCooldownHours,
            MagpieJobs.ParameterValues.DefaultRetryCooldownHours);
        var retryCooldown = TimeSpan.FromHours(retryCooldownHours);
        var backpressureThreshold = ReadPositiveIntParameter(
            context.Parameters,
            BackpressureThresholdParameter,
            DefaultBackpressureThreshold);

        var activeAcquisitions = (await jobExecutionRepository
            .QueryAsync(
                new JobExecutionQuery
                {
                    JobType = MagpieJobs.Types.ReleaseDownloadItem,
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
        if (activeAcquisitions.Count > backpressureThreshold)
        {
            logger.LogWarning(
                "{ActiveCount} ReleaseDownloadItem executions are active (threshold {Threshold})." +
                " The auto-grab scan skipped this run",
                activeAcquisitions.Count,
                backpressureThreshold);
            progress?.Report(1, "The scan stopped because the ReleaseDownloadItem queue is not empty.");
            return;
        }

        progress?.Report(0, "The scan will load the monitored content.");

        var processable = new List<ContentItem>();
        var candidates = await GetAcquisitionCandidatesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (candidate.DateLastSearched is null)
            {
                await EnqueueReleaseSearchAsync(
                    candidate.ContentHierarchyItemId,
                    userId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);

                continue;
            }

            processable.Add(candidate);
        }

        var eligible = await FilterToCooldownEligibleAsync(processable, retryCooldown, cancellationToken)
            .ConfigureAwait(false);

        var totalItems = eligible.Count;
        var completedItems = 0;
        foreach (var candidate in eligible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.ReportFraction(completedItems, totalItems);

            try
            {
                await EnqueueAutoGrabItemAsync(
                    candidate.Id,
                    userId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The auto-grab scan failed to enqueue ReleaseAutoGrabItem for content item {ContentItemId}",
                    candidate.Id);
            }

            completedItems++;
        }

        progress?.Report(1, "The release auto-grab scan completed.");
    }

    private async Task<List<ContentItem>> FilterToCooldownEligibleAsync(
        IReadOnlyList<ContentItem> candidates,
        TimeSpan cooldown,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var cutoff = timeProvider.GetUtcNow().UtcDateTime - cooldown;

        var partitionByItem = candidates.ToDictionary(
            c => c.Id,
            c => jobManager.ResolveLockPartition(
                ReleaseAutoGrabItemJob.Spec.JobType,
                new Dictionary<string, string?>
                {
                    [MagpieJobs.Parameters.ContentItemId] = c.Id.ToString()
                })!);

        IReadOnlyDictionary<string, DateTime> lastAttempts;
        try
        {
            lastAttempts = await jobExecutionRepository
                .GetLastAttemptTimesByLockPartitionAsync(
                    ReleaseAutoGrabItemJob.Spec.JobType,
                    [.. partitionByItem.Values],
                    cancellationToken)
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
                "The auto-grab scan failed to read the cooldown timestamps, so the scan will attempt" +
                " all {MatchCount} matches without the cooldown filter",
                candidates.Count);
            return [.. candidates];
        }

        var eligible = new List<ContentItem>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var partition = partitionByItem[candidate.Id];
            if (lastAttempts.TryGetValue(partition, out var lastAttempt) && lastAttempt > cutoff)
            {
                logger.LogDebug(
                    "The auto-grab scan skipped content item {ContentItemId}." +
                    " The last attempt {LastAttempt:O} is after the cooldown cutoff {Cutoff:O}",
                    candidate.Id,
                    lastAttempt,
                    cutoff);
                continue;
            }

            eligible.Add(candidate);
        }

        return eligible;
    }

    private async Task EnqueueAutoGrabItemAsync(
        Guid contentItemId,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        await jobManager.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = ReleaseAutoGrabItemJob.Spec.JobType,
                Parameters = new Dictionary<string, string>
                {
                    [MagpieJobs.Parameters.ContentItemId] = contentItemId.ToString()
                },
                Mode = JobExecutionMode.Queue,
                UserId = userId,
                CorrelationId = correlationId,
            },
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "The auto-grab scan enqueued ReleaseAutoGrabItem for content item {ContentItemId}",
            contentItemId);
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
                },
                Mode = JobExecutionMode.Queue,
                UserId = userId,
                CorrelationId = correlationId,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ContentItem>> GetAcquisitionCandidatesAsync(
        CancellationToken cancellationToken)
    {
        var candidates = new List<ContentItem>();
        foreach (var status in new[] { FulfillmentStatus.Missing, FulfillmentStatus.Upgradable })
        {
            var pageNumber = 1;
            while (true)
            {
                var page = await contentHierarchyItemRepository.QueryAsync(
                    new ContentHierarchyItemCriteria
                    {
                        LeafOnly = true,
                        EffectiveMonitoring = true,
                        HasSearchableTitle = true,
                        Fulfillment = new CriteriaSet<PropertyCriterion<FulfillmentStatus>>
                        {
                            And = [new PropertyCriterion<FulfillmentStatus>
                            {
                                Operator = CriteriaValueOperator.Equals,
                                Value = status,
                            }],
                        },
                    },
                    new PagingParameter { PageNumber = pageNumber, PageSize = PageSize },
                    sort: null,
                    cancellationToken).ConfigureAwait(false);

                foreach (var item in page.Items)
                {
                    var contentItem = item.ContentItem ?? (await contentRepository.QueryAsync(
                        new ContentItemQuery { ContentHierarchyItemId = item.Id },
                        new() { PageNumber = 1, PageSize = 2 },
                        null,
                        cancellationToken).ConfigureAwait(false)).Items.SingleOrDefault();

                    if (contentItem is not null)
                    {
                        candidates.Add(contentItem with { ContentHierarchyItemId = item.Id });
                    }
                }

                if (pageNumber * PageSize >= page.TotalItems || !page.Items.Any())
                {
                    break;
                }

                pageNumber++;
            }
        }

        return candidates;
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
