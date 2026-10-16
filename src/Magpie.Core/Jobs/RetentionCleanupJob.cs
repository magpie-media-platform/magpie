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
    IProgressReporter progressReporter) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.RetentionCleanup,
        DisplayName = "Retention cleanup",
        Description = "Deletes old job history, activity records, and releases.",
        Category = "System",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.Global,
        Parameters =
        [
            new(
                MagpieJobs.Parameters.JobExecutionRetentionDays,
                "Days to retain terminal job execution rows.",
                DataType.Number,
                false,
                "30") { ValidateScheduleValue = ValidateRetentionDays },
            new(
                MagpieJobs.Parameters.AuditRetentionDays,
                "Days to retain audit records.",
                DataType.Number,
                false,
                "90") { ValidateScheduleValue = ValidateRetentionDays },
            new(
                ReleaseObservationRetentionDaysParameter,
                "Days to retain observed release rows.",
                DataType.Number,
                false,
                "7") { ValidateScheduleValue = ValidateRetentionDays },
            new(
                MagpieJobs.Parameters.FailedDownloadRetentionDays,
                "Days to retain failed download rows.",
                DataType.Number,
                false,
                "30") { ValidateScheduleValue = ValidateRetentionDays }
        ],
    };

    private const string ReleaseObservationRetentionDaysParameter =
        MagpieJobs.Parameters.ReleaseObservationRetentionDays;

    private const int DefaultJobExecutionDays = 30;
    private const int DefaultAuditRecordDays = 90;
    private const int DefaultReleaseDays = 7;
    private const int DefaultFailedDownloadRetentionDays = 30;

    private static string? ValidateRetentionDays(
        string? value,
        IReadOnlyDictionary<string, string> parameters,
        DateTime utcNow) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) &&
        days > utcNow.Ticks / TimeSpan.TicksPerDay
            ? "exceeds the supported date range." : null;

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var jobExecutionCutoff = now -
            TimeSpan.FromDays(
                ReadRetentionDays(
                    jobContext.Parameters,
                    MagpieJobs.Parameters.JobExecutionRetentionDays,
                    DefaultJobExecutionDays));
        var auditRecordCutoff = now -
            TimeSpan.FromDays(
                ReadRetentionDays(
                    jobContext.Parameters,
                    MagpieJobs.Parameters.AuditRetentionDays,
                    DefaultAuditRecordDays));
        var releaseCutoff = now -
            TimeSpan.FromDays(
                ReadRetentionDays(jobContext.Parameters, ReleaseObservationRetentionDaysParameter, DefaultReleaseDays));
        var failedDownloadCutoff = now -
            TimeSpan.FromDays(ReadRetentionDays(
                jobContext.Parameters,
                MagpieJobs.Parameters.FailedDownloadRetentionDays,
                DefaultFailedDownloadRetentionDays));

        progressReporter.Report(null, "The retention cleanup will delete old job history.");
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
                    CompletedAt = new(null, jobExecutionCutoff),
                },
                cancellationToken)
            .ConfigureAwait(false);

        progressReporter.Report(null, "The retention cleanup will delete old activity records.");
        var auditRecordsDeleted = await auditRecordRepository
            .DeleteWhereAsync(new AuditRecordQuery { TimestampBefore = auditRecordCutoff }, cancellationToken)
            .ConfigureAwait(false);

        progressReporter.Report(null, "The retention cleanup will delete old release observations.");
        var releasesDeleted = await releaseRepository.DeleteWhereAsync(
            new ReleaseQuery { ObservedAt = new(null, releaseCutoff) },
            cancellationToken).ConfigureAwait(false);

        progressReporter.Report(null, "The retention cleanup will delete old failed downloads.");
        var failedDownloadsDeleted = await downloadService
            .DeleteClearedFailedDownloadsAsync(failedDownloadCutoff, cancellationToken)
            .ConfigureAwait(false);

        progressReporter.Report(null, "The retention cleanup completed.");

        logger.LogInformation(
            "The retention cleanup deleted {JobExecutionsDeleted} job executions before {JobExecutionCutoff:O}",
            jobExecutionsDeleted,
            jobExecutionCutoff);
        logger.LogInformation(
            "The retention cleanup deleted {AuditRecordsDeleted} activity records before {AuditRecordCutoff:O}",
            auditRecordsDeleted,
            auditRecordCutoff);
        logger.LogInformation(
            "The retention cleanup deleted {ReleasesDeleted} release observations before {ReleaseCutoff:O}",
            releasesDeleted,
            releaseCutoff);
        logger.LogInformation(
            "The retention cleanup deleted {FailedDownloadsDeleted} failed downloads before {FailedDownloadCutoff:O}",
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
            "Parameter {Parameter} value {Raw} is not a non-negative integer, so the job will use the default" +
            " {Default}",
            parameterName,
            raw,
            defaultDays);

        return defaultDays;
    }
}
