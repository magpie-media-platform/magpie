using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Abstractions.Database;

/// <summary>
/// The provider contract for database backup and restore.
/// </summary>
public interface IRecoverableDatabaseManagementProvider : IDatabaseManagementProvider
{
    /// <summary>
    /// Creates a backup of the database.
    /// </summary>
    /// <param name="options">The options that shape the new backup</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The id of the new backup</returns>
    Task<string> BackupAsync(BackupCreateOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a backup for restore on the next host startup.
    /// </summary>
    /// <param name="backupId">The id of the backup to restore</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>
    /// <see langword="true"/> when the backup exists, <see langword="false"/> when it does not.
    /// </returns>
    Task<bool> StageRestoreAsync(string backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// If a backup is marked for restore, restores the database from it.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when a restore ran, otherwise <see langword="false"/></returns>
    Task<bool> ApplyPendingRestoreAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the backups that are available to restore.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The backups that are available to restore</returns>
    Task<IEnumerable<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a backup.
    /// </summary>
    /// <param name="backupId">The id of the backup to delete</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>
    /// <see langword="true"/> when the backup exists, <see langword="false"/> when it does not.
    /// </returns>
    Task<bool> DeleteBackupAsync(string backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the path of the file that holds a backup.
    /// </summary>
    /// <param name="backupId">The id of the backup to locate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The absolute path to the backup file, or null when the backup does not exist</returns>
    Task<string?> GetBackupFilePathAsync(string backupId, CancellationToken cancellationToken = default);
}
