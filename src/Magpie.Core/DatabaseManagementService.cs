using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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
        return dbManagementProvider switch
        {
            IOptimizingDatabaseManagementProvider o => new()
            {
                Provider = dbManagementProvider.Provider,
                OptimizeSupported = true,
                LastOptimizedAt = await o.GetLastOptimizedAsync(cancellationToken).ConfigureAwait(false),
                BackupRestoreSupported = o is IRecoverableDatabaseManagementProvider
            },
            IRecoverableDatabaseManagementProvider r => new()
            {
                Provider = dbManagementProvider.Provider,
                BackupRestoreSupported = true,
            },
            _ => new()
            {
                Provider = dbManagementProvider.Provider
            }
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

    public async Task<Stream?> OpenBackupAsync(string backupId, CancellationToken cancellationToken = default)
    {
        if (dbManagementProvider is not IRecoverableDatabaseManagementProvider recoverable)
        {
            return null;
        }

        var filePath = await recoverable.GetBackupFilePathAsync(backupId, cancellationToken).ConfigureAwait(false);
        if (filePath is null || !File.Exists(filePath))
        {
            return null;
        }

        return File.OpenRead(filePath);
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

        if (!await recoverable.StageRestoreAsync(backupId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await system.RequestRestartAsync(callerUserId, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
