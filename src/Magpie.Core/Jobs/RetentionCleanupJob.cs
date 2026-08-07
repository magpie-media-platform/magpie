using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Enums;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class RetentionCleanupJob(
    IJobExecutionRepository jobExecutionRepository,
    IAuditRecordRepository auditRecordRepository,
    IReleaseRepository releaseRepository,
    IDownloadService downloadService,
    TimeProvider timeProvider,
    ILogger<RetentionCleanupJob> logger,
    IProgressReporter? progressReporter = null) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = "RetentionCleanup",
        DisplayName = "Retention cleanup",
        Description = "Deletes old job history, activity records, and releases based on retention settings.",
        Category = "System",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.Global,
        Parameters =
        [
            new(
                JobExecutionRetentionDaysParameter,
                "Days to retain terminal job execution rows.",
                DataType.Number,
                false,
                "30"),
            new(
                AuditRetentionDaysParameter,
                "Days to retain audit records.",
                DataType.Number,
                false,
                "90"),
            new(
                ReleaseObservationRetentionDaysParameter,
                "Days to retain observed release rows.",
                DataType.Number,
                false,
                "7"),
            new(
                FailedDownloadRetentionDaysParameter,
                "Days to retain failed download rows.",
                DataType.Number,
                false,
                "30")
        ],
    };

    private const string JobExecutionRetentionDaysParameter = "jobExecutionRetentionDays";
    private const string AuditRetentionDaysParameter = "auditRetentionDays";
    private const string ReleaseObservationRetentionDaysParameter = "releaseObservationRetentionDays";
    private const string FailedDownloadRetentionDaysParameter = "failedDownloadRetentionDays";
    private const int DefaultJobExecutionDays = 30;
    private const int DefaultAuditRecordDays = 90;
    private const int DefaultReleaseDays = 7;
    private const int DefaultFailedDownloadRetentionDays = 30;

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        var progress = progressReporter;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var jobExecutionCutoff = now -
            TimeSpan.FromDays(
                ReadRetentionDays(
                    jobContext.Parameters,
                    JobExecutionRetentionDaysParameter,
                    DefaultJobExecutionDays));
        var auditRecordCutoff = now -
            TimeSpan.FromDays(
                ReadRetentionDays(
                    jobContext.Parameters,
                    AuditRetentionDaysParameter,
                    DefaultAuditRecordDays));
        var releaseCutoff = now -
            TimeSpan.FromDays(
                ReadRetentionDays(
                    jobContext.Parameters,
                    ReleaseObservationRetentionDaysParameter,
                    DefaultReleaseDays));
        var failedDownloadCutoff = now -
            TimeSpan.FromDays(ReadPositiveDaysParameter(
                FailedDownloadRetentionDaysParameter,
                jobContext.Parameters,
                DefaultFailedDownloadRetentionDays));

        progress?.Report(null, "The retention cleanup will delete old job history.");
        var jobExecutionsDeleted = await jobExecutionRepository
            .DeleteWhereAsync(
                new JobExecutionQuery
                {
                    Statuses =
                    [
                        JobExecutionStatus.Completed,
                        JobExecutionStatus.Failed,
                        JobExecutionStatus.Cancelled,
                    ],
                    CompletedAt = new Range<DateTime>(null, jobExecutionCutoff),
                },
                cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(null, "The retention cleanup will delete old activity records.");
        var auditRecordsDeleted = await auditRecordRepository
            .DeleteWhereAsync(new AuditRecordQuery { TimestampBefore = auditRecordCutoff }, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(null, "The retention cleanup will delete old release observations.");
        var releasesDeleted = await releaseRepository.DeleteWhereAsync(
            new ReleaseQuery { ObservedAt = new Range<DateTime>(null, releaseCutoff) },
            cancellationToken).ConfigureAwait(false);

        progress?.Report(null, "The retention cleanup will delete old failed downloads.");
        var failedDownloadsDeleted = await downloadService
            .DeleteClearedFailedDownloadsAsync(failedDownloadCutoff, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(null, "The retention cleanup completed.");

        logger.LogInformation(
            "The retention cleanup deleted {JobExecutionsDeleted} job executions before {JobExecutionCutoff:O}.",
            jobExecutionsDeleted,
            jobExecutionCutoff);
        logger.LogInformation(
            "The retention cleanup deleted {AuditRecordsDeleted} activity records before {AuditRecordCutoff:O}.",
            auditRecordsDeleted,
            auditRecordCutoff);
        logger.LogInformation(
            "The retention cleanup deleted {ReleasesDeleted} release observations before {ReleaseCutoff:O}.",
            releasesDeleted,
            releaseCutoff);
        logger.LogInformation(
            "The retention cleanup deleted {FailedDownloadsDeleted} failed downloads before {FailedDownloadCutoff:O}.",
            failedDownloadsDeleted,
            failedDownloadCutoff);
    }

    private int ReadRetentionDays(IReadOnlyDictionary<string, string> parameters, string parameterName, int defaultDays)
    {
        if (parameters.TryGetValue(parameterName, out var raw))
        {
            return ReadNonNegativeDaysParameter(parameterName, raw, defaultDays);
        }

        return defaultDays;
    }

    private int ReadNonNegativeDaysParameter(string parameterName, string raw, int defaultDays)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured) &&
            configured >= 0)
        {
            return configured;
        }

        logger.LogWarning(
            "Parameter {Parameter} value {Raw} is not a non-negative integer. The job will use the default {Default}",
            parameterName,
            raw,
            defaultDays);

        return defaultDays;
    }

    private int ReadPositiveDaysParameter(
        string parameterName,
        IReadOnlyDictionary<string, string> parameters,
        int defaultDays)
    {
        if (!parameters.TryGetValue(parameterName, out var raw))
        {
            return defaultDays;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured) && configured > 0)
        {
            return configured;
        }

        logger.LogWarning(
            "Retention job parameter {Parameter} value {Raw} is not a positive integer. " +
            "The job will use the default {Default}",
            parameterName,
            raw,
            defaultDays);

        return defaultDays;
    }
}
