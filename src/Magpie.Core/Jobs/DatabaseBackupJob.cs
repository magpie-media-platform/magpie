using System;
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

internal sealed class DatabaseBackupJob(
    IEventBus eventBus,
    ILogger<DatabaseBackupJob> logger,
    IProgressReporter progressReporter,
    IDatabaseManagementProvider? databaseProvider = null) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.DatabaseBackup,
        DisplayName = "Database backup",
        Description = "Creates a backup of the database.",
        Category = "System",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        SupportsImmediate = false,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule],
        ConcurrencyScope = ConcurrencyScope.Global,
        Parameters =
        [
            new(
                MagpieJobs.Parameters.Name,
                "Optional backup name. When omitted, a timestamp-based name is used.",
                DataType.String,
                false,
                null)
        ],
    };

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        string? createdBackupId = null;

        if (databaseProvider is not IRecoverableDatabaseManagementProvider backupProvider)
        {
            var reason = $"Database provider {databaseProvider?.Provider ?? "none"} does not support backups.";
            logger.LogError("The database backup failed: {Reason}", reason);
            await eventBus
                .PublishAsync(DatabaseEventBuilder.BuildBackupFailedEvent(createdBackupId, reason), cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidOperationException(reason);
        }

        jobContext.Parameters.TryGetValue(MagpieJobs.Parameters.Name, out var name);
        progressReporter.Report(null, "The backup job will create a database backup.");

        try
        {
            createdBackupId = await backupProvider
                .BackupAsync(new BackupCreateOptions { Name = name, Kind = BackupKind.Manual }, cancellationToken)
                .ConfigureAwait(false);

            var path = await backupProvider
                .GetBackupFilePathAsync(createdBackupId, cancellationToken)
                .ConfigureAwait(false);
            var sizeBytes = (await backupProvider.ListBackupsAsync(cancellationToken).ConfigureAwait(false))
                .Where(backup => string.Equals(backup.Id, createdBackupId, StringComparison.Ordinal))
                .Select(backup => backup.SizeBytes)
                .FirstOrDefault();

            logger.LogInformation(
                "The backup provider created the database backup {BackupName} at {BackupPath} ({SizeBytes} bytes)",
                createdBackupId,
                path,
                sizeBytes);

            await eventBus
                .PublishAsync(
                    DatabaseEventBuilder.BuildBackupCompletedEvent(createdBackupId, path ?? string.Empty, sizeBytes),
                    cancellationToken)
                .ConfigureAwait(false);

            progressReporter.Report(1, "The database backup completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The database backup failed");
            await eventBus
                .PublishAsync(
                    DatabaseEventBuilder.BuildBackupFailedEvent(createdBackupId, ex.Message),
                    cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
    }
}
