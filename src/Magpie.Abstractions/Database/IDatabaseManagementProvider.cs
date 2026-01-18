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
    /// <param name="Id">The backup identifier.</param>
    /// <param name="CreatedAt">The backup creation timestamp.</param>
    public record BackupInfo(string Id, DateTimeOffset CreatedAt);

    /// <summary>
    /// The provider identifier.
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Creates a backup of the database.
    /// </summary>
    /// <param name="name">The backup name. If null, a timestamp-based name is generated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The backup identifier that can be used to restore.</returns>
    Task<string> BackupAsync(string? name = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores the database from a backup.
    /// </summary>
    /// <param name="backupId">The backup identifier returned from BackupAsync.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RestoreAsync(string backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists available backups.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of available backups.</returns>
    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a backup.
    /// </summary>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteBackupAsync(string backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimizes the database to reclaim space and improve performance.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OptimizeAsync(CancellationToken cancellationToken = default);
}
