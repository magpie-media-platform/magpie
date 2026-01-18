using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Configuration;
using Magpie.Abstractions.Database;
using Magpie.Abstractions.System;
using Magpie.Models.Result;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using static Magpie.Abstractions.Database.IDatabaseManagementProvider;

namespace Magpie.Data.Database.SQLite;

/// <summary>
/// SQLite database management provider with backup, restore, and vacuum operations.
/// </summary>
/// <param name="context">The database context.</param>
/// <param name="magpieSystem">The magpie system.</param>
/// <param name="systemSettings">The system settings provider.</param>
public sealed class SQLiteDatabaseManagementProvider(
    MagpieDbContext context,
    IMagpieSystem magpieSystem,
    ISystemSettingsProvider systemSettings) : IDatabaseManagementProvider
{
    /// <inheritdoc />
    public string Provider => "SQLite";

    /// <inheritdoc />
    public async Task<string> BackupAsync(string? name, CancellationToken cancellationToken)
    {
        var connectionString = context.Database.GetDbConnection().ConnectionString
            ?? throw new InvalidOperationException();
        var backupDirectory = await GetBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(backupDirectory);

        var backupId = name ?? DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupDirectory, $"{backupId}.db");
        var backupConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = backupPath
        };

        if (File.Exists(backupPath))
        {
            throw new ArgumentException($"Backup '{backupId}' already exists.", nameof(name));
        }

        using var sourceConnection = new SqliteConnection(connectionString);
        using var backupConnection = new SqliteConnection(backupConnectionString.ConnectionString);

        await sourceConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await backupConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

        sourceConnection.BackupDatabase(backupConnection);

        return backupId;
    }

    /// <inheritdoc />
    public async Task RestoreAsync(string backupId, CancellationToken cancellationToken)
    {
        var connectionString = context.Database.GetDbConnection().ConnectionString
            ?? throw new InvalidOperationException();
        var backupDirectory = await GetBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);
        var backupPath = Path.Combine(backupDirectory, $"{backupId}.db");

        var backupConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = backupPath
        };

        if (!File.Exists(backupPath))
        {
            throw new ArgumentException($"Backup '{backupId}' not found.", nameof(backupId));
        }

        using var destinationConnection = new SqliteConnection(connectionString);
        using var backupConnection = new SqliteConnection(backupConnectionString.ConnectionString);

        await destinationConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await backupConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

        backupConnection.BackupDatabase(destinationConnection);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken)
    {
        var backupDirectory = await GetBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);

        if (!Directory.Exists(backupDirectory))
        {
            return [];
        }

        var backups = (
            from path in Directory.GetFiles(backupDirectory, "*.db")
            let fileName = Path.GetFileNameWithoutExtension(path)
            let createdAt = File.GetCreationTimeUtc(path)
            orderby createdAt descending
            select new BackupInfo(fileName, new DateTimeOffset(createdAt, TimeSpan.Zero))).ToList();

        return backups;
    }

    /// <inheritdoc />
    public async Task DeleteBackupAsync(string backupId, CancellationToken cancellationToken)
    {
        var backupDirectory = await GetBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);
        var backupPath = Path.Combine(backupDirectory, $"{backupId}.db");

        if (!File.Exists(backupPath))
        {
            throw new ArgumentException($"Backup '{backupId}' not found.", nameof(backupId));
        }

        File.Delete(backupPath);
    }

    /// <inheritdoc />
    public async Task OptimizeAsync(CancellationToken cancellationToken)
    {
        var connectionString = context.Database.GetDbConnection().ConnectionString
            ?? throw new InvalidOperationException();

        using var connection = new SqliteConnection(connectionString);

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM; PRAGMA wal_checkpoint(TRUNCATE);";

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetBackupDirectoryAsync(CancellationToken cancellationToken)
    {
        var result = await systemSettings.GetSettingAsync(
            Constants.SettingsGroupName,
            Constants.BackupDirectorySettingKey,
            Constants.DefaultBackupDirectory,
            cancellationToken).ConfigureAwait(false);

        if (result is not SuccessResult<string?> success)
        {
            throw new InvalidOperationException($"Failed to get backup directory setting: {result}");
        }

        var backupFolder = success.Value ?? Constants.DefaultBackupDirectory;

        if (!Path.IsPathRooted(backupFolder))
        {
            backupFolder = Path.Combine(magpieSystem.SystemRoot, backupFolder);
        }

        return backupFolder;
    }
}
