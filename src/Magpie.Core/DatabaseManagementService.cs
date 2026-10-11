using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Database;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Sdk.Services;

namespace Magpie.Core;

internal sealed class DatabaseManagementService(
    IDatabaseManagementProvider dbManagementProvider,
    IMagpieSystem system,
    IAuditRecordRepository auditRecordRepository,
    IEventBus eventBus,
    TimeProvider timeProvider) : IDatabaseManagementService
{
    public async Task<DatabaseManagementInfo> GetInfo(CancellationToken cancellationToken = default)
    {
        var optimizing = dbManagementProvider as IOptimizingDatabaseManagementProvider;
        var recoverable = dbManagementProvider as IRecoverableDatabaseManagementProvider;

        return new()
        {
            Provider = dbManagementProvider.Provider,
            OptimizeSupported = optimizing is not null,
            LastOptimizedAt = optimizing is null
                ? null
                : await optimizing.GetLastOptimizedAsync(cancellationToken).ConfigureAwait(false),
            BackupRestoreSupported = recoverable is not null,
            BackupDeniedReason = recoverable is null
                ? null
                : await recoverable.GetBackupDeniedReasonAsync(cancellationToken).ConfigureAwait(false),
            RestoreDeniedReason = recoverable is null
                ? null
                : await recoverable.GetRestoreDeniedReasonAsync(cancellationToken).ConfigureAwait(false),
            DeleteDeniedReason = recoverable is null
                ? null
                : await recoverable.GetDeleteDeniedReasonAsync(cancellationToken).ConfigureAwait(false)
        };
    }

    public async Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        if (dbManagementProvider is not IRecoverableDatabaseManagementProvider recoverable)
        {
            return [];
        }

        return [.. await recoverable.ListBackupsAsync(cancellationToken).ConfigureAwait(false)];
    }

    public async Task<bool> DeleteBackupAsync(
        string backupId,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        if (dbManagementProvider is not IRecoverableDatabaseManagementProvider recoverable)
        {
            return false;
        }

        var deleted = await recoverable.DeleteBackupAsync(backupId, cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            await auditRecordRepository.CreateAsync(
                new()
                {
                    Category = AuditCategories.System,
                    Operation = "BackupDeleted",
                    EntityType = "Backup",
                    EntityId = backupId,
                    Detail = $"Database backup {backupId} deleted.",
                    Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                    UserId = callerUserId
                },
                cancellationToken).ConfigureAwait(false);
            await eventBus.PublishAsync(DatabaseEventBuilder.BuildBackupDeletedEvent(backupId), cancellationToken)
                .ConfigureAwait(false);
        }

        return deleted;
    }

    public async Task<bool> RestoreBackupAsync(
        string backupId,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        if (dbManagementProvider is not IRecoverableDatabaseManagementProvider recoverable)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!system.GetInfo().RestartSupported)
        {
            throw new MagpieShutdownException("The host is not supervised.");
        }

        if (!await recoverable.StageRestoreAsync(backupId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await system.RequestRestartAsync(callerUserId, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
