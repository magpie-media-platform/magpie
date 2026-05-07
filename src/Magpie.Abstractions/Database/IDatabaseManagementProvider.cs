using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Database;

/// <summary>
/// Provides database management functionality for a database provider.
/// </summary>
public interface IDatabaseManagementProvider
{
    /// <summary>
    /// Information about a database backup.
    /// </summary>
    /// <param name="Id">The backup id.</param>
    /// <param name="Name">The human-readable backup name.</param>
    /// <param name="CreatedAt">The backup creation timestamp.</param>
    /// <param name="SizeBytes">The backup file size in bytes.</param>
    public record BackupInfo(string Id, string Name, DateTime CreatedAt, long SizeBytes);

    /// <summary>
    /// The provider id.
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Create a backup of the database.
    /// </summary>
    /// <param name="name">The backup name. If null, a timestamp-based name is generated.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The backup id that can be used to restore.</returns>
    Task<string> BackupAsync(string? name = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restore the database from a backup.
    /// </summary>
    /// <param name="backupId">The backup id returned from BackupAsync.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the backup was restored; false if the backup was not found.</returns>
    Task<bool> RestoreAsync(string backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists available backups.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of available backups.</returns>
    Task<IEnumerable<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a backup.
    /// </summary>
    /// <param name="backupId">The backup id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the backup was deleted; false if the backup was not found.</returns>
    Task<bool> DeleteBackupAsync(string backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the file path for a backup.
    /// </summary>
    /// <param name="backupId">The backup id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The full file path to the backup file, or null if the backup was not found.</returns>
    Task<string?> GetBackupFilePathAsync(string backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimize the database to reclaim space and improve performance.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task OptimizeAsync(CancellationToken cancellationToken = default);
}
