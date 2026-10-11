using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for the Magpie database and its backups.
/// </summary>
public interface IDatabaseManagementService
{
    /// <summary>
    /// Gets the provider, size, and backup state of the database.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<DatabaseManagementInfo> GetInfo(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the available backups.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a backup.
    /// </summary>
    /// <param name="backupId">The id of the backup to delete</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteBackupAsync(
        string backupId,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a backup for restore and requests the host restart that applies it.
    /// </summary>
    /// <param name="backupId">The id of the backup to restore</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> RestoreBackupAsync(
        string backupId,
        Guid callerUserId,
        CancellationToken cancellationToken = default);
}
