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
using Magpie.Sdk.Events;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class DownloadImportScanJob(
    IDownloadRepository downloadRepository,
    IJobExecutionRepository jobExecutionRepository,
    IJobManager jobManager,
    TimeProvider timeProvider,
    ILogger<DownloadImportScanJob> logger,
    IProgressReporter progressReporter) : IJob
{
    private static readonly TimeSpan SettleWindow = TimeSpan.FromMinutes(DefaultSettleMinutes);

    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.DownloadImportScan,
        DisplayName = "Download import scan",
        Description = "Finds downloads that remain in Downloaded when Magpie misses their DownloadImportItem " +
            "event. Queues one DownloadImportItem job for each download.",
        Category = "System",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters =
        [
            new(
                MagpieJobs.Parameters.SettleMinutes,
                "Minutes a downloaded row must remain unchanged before recovery.",
                DataType.Number,
                false,
                "10")
            {
                ValidateScheduleValue = (value, _, utcNow) =>
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
                    minutes > utcNow.Ticks / TimeSpan.TicksPerMinute
                        ? "exceeds the supported date range." : null
            }
        ],
    };

    private const int DefaultSettleMinutes = 10;

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        progressReporter.Report(0, "The recovery will search for downloads without an import job.");

        var cutoff = timeProvider.GetUtcNow().UtcDateTime - ResolveSettleWindow(jobContext.Parameters);

        var downloads = await downloadRepository.QueryAllAsync(
            new DownloadQuery
            {
                Statuses = [DownloadStatus.Downloaded, DownloadStatus.Importing],
                LastChangedAt = new(null, cutoff)
            },
            new()
            {
                PropertyName = nameof(DownloadQuery.LastChangedAt),
                Direction = SortDirection.Ascending
            },
            cancellationToken).ConfigureAwait(false);

        var committedRecovery = await downloadRepository.QueryAllAsync(
            new DownloadQuery
            {
                Status = DownloadStatus.Imported,
                HasImportRecoveryState = true,
                LastChangedAt = new(null, cutoff)
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        downloads = downloads.Concat(committedRecovery).DistinctBy(download => download.Id).ToList();

        if (downloads.Count == 0)
        {
            progressReporter.Report(1, "The download recovery completed.");
            return;
        }

        // Text reference filters do not match all GUID forms that the item job accepts.
        var downloadIds = downloads.Select(download => download.Id).ToHashSet();
        var history = (await jobExecutionRepository.QueryAllAsync(
            new JobExecutionQuery { JobType = MagpieJobs.Types.DownloadImportItem },
            cancellationToken: cancellationToken).ConfigureAwait(false))
            .Where(execution => ResolveDownloadId(execution) is { } id && downloadIds.Contains(id))
            .ToLookup(ResolveDownloadId);

        var enqueued = 0;
        var totalDownloads = downloads.Count;
        for (var i = 0; i < downloads.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var download = downloads[i];
            progressReporter.Report(
                (i + 1) / (double)(totalDownloads + 1),
                $"The recovery will process download {download.Id}.");

            try
            {
                var executions = history[download.Id];
                if (executions.Any(execution => execution.Status is JobExecutionStatus.Queued or
                    JobExecutionStatus.Running or JobExecutionStatus.CancellationRequested))
                {
                    continue;
                }

                var active = await jobExecutionRepository.QueryAllAsync(
                    new JobExecutionQuery
                    {
                        JobType = MagpieJobs.Types.DownloadImportItem,
                        Statuses =
                        [
                            JobExecutionStatus.Queued,
                            JobExecutionStatus.Running,
                            JobExecutionStatus.CancellationRequested
                        ]
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (active.Any(execution =>
                    execution.Status is JobExecutionStatus.Queued or JobExecutionStatus.Running or
                        JobExecutionStatus.CancellationRequested && ResolveDownloadId(execution) == download.Id))
                {
                    continue;
                }

                var previous = executions.Where(execution =>
                        !execution.Parameters.TryGetValue(MagpieJobs.Parameters.DryRun, out var rawDryRun) ||
                        !bool.TryParse(rawDryRun, out var dryRun) || !dryRun)
                    .OrderByDescending(execution => execution.CreatedAt)
                    .ThenByDescending(execution => execution.Id).FirstOrDefault();
                var parameters = new Dictionary<string, string>()
                {
                    [MagpieJobs.Parameters.DownloadId] = download.Id.ToString("D")
                };
                if (download.Status == DownloadStatus.Importing && download.ImportCollisionMode is { } storedMode)
                {
                    parameters[MagpieJobs.Parameters.CollisionMode] = storedMode.ToString();
                }
                else if (previous is not null && previous.Parameters.TryGetValue(
                    MagpieJobs.Parameters.CollisionMode,
                    out var collisionMode) &&
                    !string.IsNullOrWhiteSpace(collisionMode))
                {
                    parameters[MagpieJobs.Parameters.CollisionMode] = collisionMode;
                }

                await jobManager.ExecuteAsync(
                    new JobEnqueueRequest
                    {
                        JobType = MagpieJobs.Types.DownloadImportItem,
                        Parameters = parameters,
                        Mode = JobExecutionMode.Queue,
                        OnlyIfInactive = true,
                        UserId = previous?.CreatedByUserId,
                        CorrelationId = jobContext.CorrelationId,
                    },
                    cancellationToken).ConfigureAwait(false);
                enqueued++;
                logger.LogInformation(
                    "The job recovered orphaned download {DownloadId} and queued DownloadImportItem",
                    download.Id);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The job failed to queue DownloadImportItem for orphaned download {DownloadId}",
                    download.Id);
            }
        }

        progressReporter.Report(1, "The download recovery completed.");
        logger.LogInformation(
            "The job queued {Enqueued} DownloadImportItem execution(s) from {DownloadCount} candidate(s)",
            enqueued,
            totalDownloads);
    }

    private static Guid? ResolveDownloadId(JobExecution execution)
    {
        var rawId = execution.Parameters.TryGetValue(MagpieJobs.Parameters.DownloadId, out var parameter) &&
            !string.IsNullOrWhiteSpace(parameter)
            ? parameter
            : execution.TriggerContext.GetValueOrDefault(MagpieEvents.Keys.DownloadId.ToEventContextKey());
        return Guid.TryParse(rawId, out var id) ? id : null;
    }

    private TimeSpan ResolveSettleWindow(IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters.TryGetValue(MagpieJobs.Parameters.SettleMinutes, out var raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured) &&
            configured > 0)
        {
            return TimeSpan.FromMinutes(configured);
        }

        if (parameters.ContainsKey(MagpieJobs.Parameters.SettleMinutes))
        {
            logger.LogWarning(
                "Parameter {Parameter} value {Raw} is not a positive integer, so the job will use the default" +
                " {Default}",
                MagpieJobs.Parameters.SettleMinutes,
                parameters[MagpieJobs.Parameters.SettleMinutes],
                DefaultSettleMinutes);
        }

        return SettleWindow;
    }
}
