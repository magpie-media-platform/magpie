using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Database;
using Magpie.Core.Events;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class DatabaseMaintenanceJob(
    TimeProvider timeProvider,
    ILogger<DatabaseMaintenanceJob> logger,
    IEventBus eventBus,
    IDatabaseManagementProvider? databaseProvider = null,
    IProgressReporter? progressReporter = null) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = "DatabaseMaintenance",
        DisplayName = "Database maintenance",
        Description = "Runs scheduled backups, removes expired backups, and optimizes the database.",
        Category = "System",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        Timeout = TimeSpan.FromMinutes(30),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.Global,
        Parameters =
        [
            new(
                BackupEnabledParameter,
                "Whether scheduled database backups are enabled.",
                DataType.Boolean,
                false,
                DefaultBackupEnabled.ToString().ToLowerInvariant()),
            new(
                BackupIntervalDaysParameter,
                "Minimum days between scheduled database backups.",
                DataType.Number,
                false,
                DefaultBackupIntervalDays.ToString(CultureInfo.InvariantCulture)),
            new(
                BackupRetentionCountParameter,
                "Scheduled database backups to retain.",
                DataType.Number,
                false,
                DefaultBackupRetentionCount.ToString(CultureInfo.InvariantCulture)),
            new(
                OptimizeEnabledParameter,
                "Whether scheduled database optimization is enabled.",
                DataType.Boolean,
                false,
                DefaultOptimizeEnabled.ToString().ToLowerInvariant()),
            new(
                OptimizeIntervalDaysParameter,
                "Minimum days between database optimizations.",
                DataType.Number,
                false,
                DefaultOptimizeIntervalDays.ToString(CultureInfo.InvariantCulture))
        ],
    };

    private const string BackupEnabledParameter = "backupEnabled";
    private const string BackupIntervalDaysParameter = "backupIntervalDays";
    private const string BackupRetentionCountParameter = "backupRetentionCount";
    private const string OptimizeEnabledParameter = "optimizeEnabled";
    private const string OptimizeIntervalDaysParameter = "optimizeIntervalDays";

    private const bool DefaultBackupEnabled = false;
    private const int DefaultBackupIntervalDays = 1;
    private const int DefaultBackupRetentionCount = 7;
    private const bool DefaultOptimizeEnabled = true;
    private const int DefaultOptimizeIntervalDays = 7;

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        var progress = progressReporter;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var backupEnabled = ReadBooleanParameter(jobContext.Parameters, BackupEnabledParameter, DefaultBackupEnabled);
        var optimizeEnabled = ReadBooleanParameter(
            jobContext.Parameters,
            OptimizeEnabledParameter,
            DefaultOptimizeEnabled);

        progress?.Report(0, "The maintenance job will read the database capabilities.");

        if (backupEnabled)
        {
            await RunBackupMaintenanceAsync(
                jobContext.Parameters,
                now,
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            logger.LogInformation("Scheduled database backups are disabled");
        }

        if (optimizeEnabled)
        {
            await RunOptimizationMaintenanceAsync(
                jobContext.Parameters,
                now,
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            logger.LogInformation("Scheduled database optimization is disabled");
        }

        progress?.Report(1, "The database maintenance completed.");
    }

    private async Task RunBackupMaintenanceAsync(
        IReadOnlyDictionary<string, string> parameters,
        DateTime now,
        IProgressReporter? progress,
        CancellationToken cancellationToken)
    {
        if (databaseProvider is not IRecoverableDatabaseManagementProvider backupProvider)
        {
            logger.LogInformation(
                "Provider {Provider} does not support backups. The database backup maintenance did not run",
                databaseProvider?.Provider ?? "none");
            progress?.Report(
                null,
                "The maintenance job skipped database backups because the provider does not support them.");
            return;
        }

        var interval = TimeSpan.FromDays(ReadPositiveIntParameter(
            parameters,
            BackupIntervalDaysParameter,
            DefaultBackupIntervalDays));
        var retentionCount = ReadPositiveIntParameter(
            parameters,
            BackupRetentionCountParameter,
            DefaultBackupRetentionCount);

        progress?.Report(null, "The maintenance job will load the scheduled database backups.");
        var scheduledBackups = (await backupProvider.ListBackupsAsync(cancellationToken).ConfigureAwait(false))
            .Where(backup => backup.Kind == BackupKind.Scheduled)
            .OrderByDescending(backup => backup.CreatedAt)
            .ToList();

        if (scheduledBackups.FirstOrDefault() is not { } latest || latest.CreatedAt <= now - interval)
        {
            progress?.Report(null, "The maintenance job will create a scheduled database backup.");
            var backupId = await backupProvider.BackupAsync(
                new BackupCreateOptions { Kind = BackupKind.Scheduled },
                cancellationToken).ConfigureAwait(false);
            var path = await backupProvider.GetBackupFilePathAsync(backupId, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("The provider created the scheduled database backup {BackupId}", backupId);

            scheduledBackups = [..
                from backup in await backupProvider.ListBackupsAsync(cancellationToken).ConfigureAwait(false)
                where backup.Kind == BackupKind.Scheduled
                orderby backup.CreatedAt descending
                select backup];

            var created = scheduledBackups.FirstOrDefault(backup =>
                string.Equals(backup.Id, backupId, StringComparison.Ordinal));
            await eventBus.PublishAsync(
                DatabaseEventBuilder.BuildBackupCompletedEvent(backupId, path ?? string.Empty, created?.SizeBytes ?? 0),
                cancellationToken).ConfigureAwait(false);
        }

        var backupsToDelete = scheduledBackups.Skip(retentionCount).ToList();
        for (var i = 0; i < backupsToDelete.Count; i++)
        {
            var backup = backupsToDelete[i];
            progress?.ReportFraction(
                i,
                backupsToDelete.Count,
                "The maintenance job will delete old scheduled database backups.");
            var deleted = await backupProvider.DeleteBackupAsync(backup.Id, cancellationToken).ConfigureAwait(false);
            if (deleted)
            {
                logger.LogInformation("The provider deleted the old scheduled database backup {BackupId}", backup.Id);
                await eventBus.PublishAsync(DatabaseEventBuilder.BuildBackupDeletedEvent(backup.Id), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task RunOptimizationMaintenanceAsync(
        IReadOnlyDictionary<string, string> parameters,
        DateTime now,
        IProgressReporter? progress,
        CancellationToken cancellationToken)
    {
        if (databaseProvider is not IOptimizingDatabaseManagementProvider optimizingProvider)
        {
            logger.LogInformation(
                "Provider {Provider} does not support optimization. The database optimization did not run",
                databaseProvider?.Provider ?? "none");
            progress?.Report(
                null,
                "The maintenance job skipped database optimization because the provider does not support it.");
            return;
        }

        var interval = TimeSpan.FromDays(ReadPositiveIntParameter(
            parameters,
            OptimizeIntervalDaysParameter,
            DefaultOptimizeIntervalDays));
        var lastOptimized = await optimizingProvider.GetLastOptimizedAsync(cancellationToken).ConfigureAwait(false);
        if (lastOptimized is { } optimizedAt && optimizedAt > now - interval)
        {
            logger.LogInformation(
                "The database optimization did not run because it last ran at {OptimizedAt}, " +
                "inside the configured interval",
                optimizedAt);
            return;
        }

        progress?.Report(null, "The maintenance job will optimize the database.");
        await optimizingProvider.OptimizeAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("The provider completed the database optimization");
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

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return value;
        }

        logger.LogWarning(
            "Parameter {Parameter} value {Value} is not a positive integer. The job will use the default {Default}",
            parameterName,
            raw,
            defaultValue);

        return defaultValue;
    }

    private bool ReadBooleanParameter(
        IReadOnlyDictionary<string, string> parameters,
        string parameterName,
        bool defaultValue)
    {
        if (!parameters.TryGetValue(parameterName, out var raw))
        {
            return defaultValue;
        }

        if (bool.TryParse(raw, out var value))
        {
            return value;
        }

        logger.LogWarning(
            "Database maintenance parameter {Parameter} value {Value} is not boolean. " +
            "The job will use the default {Default}",
            parameterName,
            raw,
            defaultValue);

        return defaultValue;
    }
}
