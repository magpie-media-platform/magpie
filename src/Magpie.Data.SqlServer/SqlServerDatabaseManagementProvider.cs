using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Database;
using Magpie.Models;
using Magpie.Models.Enums;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Data.SqlServer;

internal sealed class SqlServerDatabaseManagementProvider(
    MagpieDbContext context,
    MagpieHostEnvironment hostEnvironment,
    TimeProvider timeProvider,
    ILogger<SqlServerDatabaseManagementProvider> logger) :
        IRecoverableDatabaseManagementProvider,
        IOptimizingDatabaseManagementProvider
{
    private const string MasterDatabase = "master";
    private const string RestoreMarkerFileName = "restore-pending.json";
    private const string OptimizedMarkerFileName = "db-optimized.json";
    private const string RollbackBackupSuffix = "_rollback.bak";
    private const int MaxBackupNameLength = 128;

    private const string ListBackupsSql =
        """
        SELECT b.name, b.description, m.physical_device_name, COALESCE(b.compressed_backup_size, b.backup_size)
        FROM msdb.dbo.backupset AS b
        JOIN msdb.dbo.backupmediafamily AS m ON m.media_set_id = b.media_set_id
        CROSS APPLY sys.dm_os_file_exists(m.physical_device_name) AS f
        WHERE b.database_name = @database AND b.type = 'D' AND b.is_copy_only = 1 AND f.file_exists = 1
            AND EXISTS (
                SELECT 1
                FROM msdb.dbo.restorehistory AS r
                WHERE r.backup_set_id = b.backup_set_id AND r.restore_type = 'V');
        """;

    private const string BackupDirectorySql =
        "SELECT CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultBackupPath'));";

    private const string BackupPermissionsSql =
        """
        SELECT
            HAS_PERMS_BY_NAME(@database, N'DATABASE', N'BACKUP DATABASE'),
            HAS_PERMS_BY_NAME(N'master', N'DATABASE', N'CREATE DATABASE');
        """;

    private const string DeletePermissionSql = "SELECT IS_SRVROLEMEMBER('sysadmin');";

    private const string BackupSql =
        "BACKUP DATABASE @database TO DISK = @path WITH COPY_ONLY, CHECKSUM, INIT, NAME = @name, DESCRIPTION = @kind;";

    private const string RollbackBackupSql =
        "BACKUP DATABASE @database TO DISK = @path WITH COPY_ONLY, CHECKSUM, INIT;";

    private const string VerifySql = "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;";

    private const string VerifyHistorySql = "RESTORE VERIFYONLY FROM DISK = @path WITH LOADHISTORY;";

    private const string DeleteFileSql = "EXEC master.sys.xp_delete_file 0, @path;";

    private const string RestorePermissionsSql =
        """
        SELECT
            CASE
                WHEN IS_SRVROLEMEMBER('sysadmin') = 1 OR IS_SRVROLEMEMBER('dbcreator') = 1 OR d.owner_sid = SUSER_SID()
                THEN 1 ELSE 0
            END,
            HAS_PERMS_BY_NAME(@database, N'DATABASE', N'BACKUP DATABASE')
        FROM sys.databases AS d
        WHERE d.name = @database;
        """;

    private const string RestoreSql =
        """
        IF DATABASEPROPERTYEX(@database, 'Status') = 'ONLINE'
        BEGIN
            DECLARE @sql nvarchar(max) =
                N'ALTER DATABASE ' + QUOTENAME(@database) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE';
            EXEC sys.sp_executesql @sql;
        END
        RESTORE DATABASE @database FROM DISK = @path WITH REPLACE, CHECKSUM;
        """;

    private const string MultiUserSql =
        """
        IF DATABASEPROPERTYEX(@database, 'Status') = 'ONLINE'
            AND DATABASEPROPERTYEX(@database, 'UserAccess') <> 'MULTI_USER'
        BEGIN
            DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@database) + N' SET MULTI_USER';
            EXEC sys.sp_executesql @sql;
        END
        """;

    private const string OptimizeSql =
        """
        DECLARE @sql nvarchar(max);
        SELECT @sql = STRING_AGG(
            CONVERT(nvarchar(max), N'ALTER INDEX ALL ON ' + n.name + N' REORGANIZE;') +
                N' UPDATE STATISTICS ' + n.name + N';',
            N' ')
        FROM sys.tables AS t
        CROSS APPLY (SELECT QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) AS name) AS n
        WHERE t.is_ms_shipped = 0;
        EXEC sys.sp_executesql @sql;
        """;

    private const string BackupIdFormat = "yyyyMMdd_HHmmss";

    private static readonly string[] BackupIdFormats = [BackupIdFormat, BackupIdFormat + "_fff"];

    private static readonly Regex BackupFileName = new(
        @"_(?<id>\d{8}_\d{6}(_\d{3})?)\.bak$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Provider => "MSSQL";

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        return await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<string>> GetAppliedMigrationsAsync(
        CancellationToken cancellationToken = default)
    {
        var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
        return [.. applied];
    }

    public async Task<string> BackupAsync(BackupCreateOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var database = GetDatabaseName();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var timestamp = now.ToString(BackupIdFormat, CultureInfo.InvariantCulture);
        var backups = await ReadBackupsAsync(database, cancellationToken).ConfigureAwait(false);
        var backupId = backups.Any(backup => backup.Info.Id == timestamp)
            ? $"{timestamp}_{now.Millisecond:D3}"
            : timestamp;
        var name = string.IsNullOrWhiteSpace(options.Name) ? $"{options.Kind} {timestamp}" : options.Name.Trim();

        if (await GetBackupDeniedReasonAsync(cancellationToken).ConfigureAwait(false) is { } deniedReason)
        {
            throw new MagpieException(deniedReason);
        }

        var directory = (await ExecuteAsync(MasterDatabase, BackupDirectorySql, [], cancellationToken)
                .ConfigureAwait(false))[0][0] as string
            ?? throw new MagpieException("The SQL Server instance has no default backup directory.");
        var path = CombineServerPath(directory, $"{database}_{backupId}.bak");

        await ExecuteAsync(
            MasterDatabase,
            BackupSql,
            [
                new("@database", database),
                new("@path", path),
                new("@name", name[..Math.Min(name.Length, MaxBackupNameLength)]),
                new("@kind", options.Kind.ToString())
            ],
            cancellationToken).ConfigureAwait(false);

        try
        {
            await ExecuteAsync(MasterDatabase, VerifySql, [new("@path", path)], cancellationToken)
                .ConfigureAwait(false);

            await ExecuteAsync(MasterDatabase, VerifyHistorySql, [new("@path", path)], cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await TryDeleteFileAsync(path, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return backupId;
    }

    public async Task<bool> StageRestoreAsync(string backupId, CancellationToken cancellationToken = default)
    {
        var path = await GetBackupFilePathAsync(backupId, cancellationToken).ConfigureAwait(false);
        if (path is null)
        {
            return false;
        }

        if (await GetRestoreDeniedReasonAsync(cancellationToken).ConfigureAwait(false) is { } deniedReason)
        {
            throw new MagpieException(deniedReason);
        }

        await WriteJsonAsync(
            GetRestoreMarkerPath(),
            new RestoreMarker { BackupId = backupId },
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Staged backup {BackupId} for restore at next host startup.", backupId);

        return true;
    }

    public async Task<bool> ApplyPendingRestoreAsync(CancellationToken cancellationToken = default)
    {
        var markerPath = GetRestoreMarkerPath();
        if (!File.Exists(markerPath))
        {
            return false;
        }

        var marker = await ReadJsonAsync<RestoreMarker>(markerPath, cancellationToken).ConfigureAwait(false);
        var database = GetDatabaseName();
        var path = marker is null
            ? null
            : await GetBackupFilePathAsync(marker.BackupId, cancellationToken).ConfigureAwait(false);
        if (marker is null || path is null)
        {
            logger.LogError(
                "Restore marker {MarkerPath} does not identify an available backup; restore stopped.",
                markerPath);
            File.Delete(markerPath);
            return false;
        }

        var rollbackPath = path[..(path.LastIndexOfAny(['/', '\\']) + 1)] + database + RollbackBackupSuffix;
        if (!marker.RollbackSaved)
        {
            try
            {
                await ExecuteAsync(
                    MasterDatabase,
                    RollbackBackupSql,
                    [new("@database", database), new("@path", rollbackPath)],
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex,
                    "Restore of backup {BackupId} stopped before the database changed.",
                    marker.BackupId);
                File.Delete(markerPath);
                return false;
            }

            marker = marker with { RollbackSaved = true };
            await WriteJsonAsync(markerPath, marker, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("Applying restore of backup {BackupId}.", marker.BackupId);

        try
        {
            await RestoreAsync(database, path, cancellationToken).ConfigureAwait(false);

            var knownMigrations = context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
            var unknown = (await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false))
                .Where(migration => !knownMigrations.Contains(migration))
                .ToArray();
            if (unknown.Length > 0)
            {
                throw new InvalidOperationException(
                    $"The restored database contains unknown migrations: {string.Join(", ", unknown)}.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Restore of backup {BackupId} failed; rolling back to the original database from {RollbackPath}.",
                marker.BackupId,
                rollbackPath);

            try
            {
                await RestoreAsync(database, rollbackPath, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                File.Delete(markerPath);
            }

            await TryDeleteFileAsync(rollbackPath, cancellationToken).ConfigureAwait(false);
            return false;
        }

        File.Delete(markerPath);
        await TryDeleteFileAsync(rollbackPath, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Applied restore of backup {BackupId}.", marker.BackupId);

        return true;
    }

    public async Task<IEnumerable<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        var backups = await ReadBackupsAsync(GetDatabaseName(), cancellationToken).ConfigureAwait(false);

        return [.. backups.Select(backup => backup.Info).OrderByDescending(backup => backup.CreatedAt)];
    }

    public async Task<bool> DeleteBackupAsync(string backupId, CancellationToken cancellationToken = default)
    {
        var path = await GetBackupFilePathAsync(backupId, cancellationToken).ConfigureAwait(false);
        if (path is null)
        {
            return false;
        }

        if (await GetDeleteDeniedReasonAsync(cancellationToken).ConfigureAwait(false) is { } deniedReason)
        {
            throw new MagpieException(deniedReason);
        }

        await ExecuteAsync(MasterDatabase, DeleteFileSql, [new("@path", path)], cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    public async Task<string?> GetBackupFilePathAsync(string backupId, CancellationToken cancellationToken = default)
    {
        var backups = await ReadBackupsAsync(GetDatabaseName(), cancellationToken).ConfigureAwait(false);

        return backups.Where(backup => backup.Info.Id == backupId).Select(backup => backup.Path).FirstOrDefault();
    }

    public async Task<string?> GetBackupDeniedReasonAsync(CancellationToken cancellationToken = default)
    {
        var permissions = (await ExecuteAsync(
            MasterDatabase,
            BackupPermissionsSql,
            [new("@database", GetDatabaseName())],
            cancellationToken).ConfigureAwait(false))[0];
        if (Convert.ToInt32(permissions[0], CultureInfo.InvariantCulture) != 1)
        {
            return "Create needs the BACKUP DATABASE permission on SQL Server";
        }

        return Convert.ToInt32(permissions[1], CultureInfo.InvariantCulture) == 1
            ? null
            : "Create needs the CREATE DATABASE permission on SQL Server";
    }

    public async Task<string?> GetRestoreDeniedReasonAsync(CancellationToken cancellationToken = default)
    {
        var permissions = (await ExecuteAsync(
            MasterDatabase,
            RestorePermissionsSql,
            [new("@database", GetDatabaseName())],
            cancellationToken).ConfigureAwait(false))[0];
        if (Convert.ToInt32(permissions[0], CultureInfo.InvariantCulture) != 1)
        {
            return "Restore needs the database owner, dbcreator or sysadmin role on SQL Server";
        }

        return Convert.ToInt32(permissions[1], CultureInfo.InvariantCulture) == 1
            ? null
            : "Restore needs the BACKUP DATABASE permission on SQL Server";
    }

    public async Task<string?> GetDeleteDeniedReasonAsync(CancellationToken cancellationToken = default)
    {
        var permission = (await ExecuteAsync(MasterDatabase, DeletePermissionSql, [], cancellationToken)
            .ConfigureAwait(false))[0][0];

        return Convert.ToInt32(permission, CultureInfo.InvariantCulture) == 1
            ? null
            : "Delete needs the sysadmin role on SQL Server";
    }

    public async Task<DateTime?> GetLastOptimizedAsync(CancellationToken cancellationToken = default)
    {
        var marker = await ReadJsonAsync<OptimizationMarker>(
            Path.Combine(hostEnvironment.DataRoot, OptimizedMarkerFileName),
            cancellationToken).ConfigureAwait(false);

        return marker?.OptimizedAtUtc;
    }

    public async Task OptimizeAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(GetDatabaseName(), OptimizeSql, [], cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(hostEnvironment.DataRoot, OptimizedMarkerFileName),
            new OptimizationMarker { OptimizedAtUtc = timeProvider.GetUtcNow().UtcDateTime },
            cancellationToken).ConfigureAwait(false);
    }

    private static string CombineServerPath(string directory, string fileName)
    {
        var separator = directory.Contains('/', StringComparison.Ordinal) ? '/' : '\\';

        return directory.TrimEnd('/', '\\') + separator + fileName;
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(value), cancellationToken)
            .ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }

    private async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Reading marker {MarkerPath} failed.", path);
            return null;
        }
    }

    private string GetRestoreMarkerPath() => Path.Combine(hostEnvironment.DataRoot, RestoreMarkerFileName);

    private string GetDatabaseName()
    {
        var database = new SqlConnectionStringBuilder(context.Database.GetConnectionString()).InitialCatalog;

        return string.IsNullOrEmpty(database)
            ? throw new InvalidOperationException("The SQL Server connection string has no database.")
            : database;
    }

    private async Task<List<(BackupInfo Info, string Path)>> ReadBackupsAsync(
        string database,
        CancellationToken cancellationToken)
    {
        var rows = await ExecuteAsync(
            MasterDatabase,
            ListBackupsSql,
            [new("@database", database)],
            cancellationToken).ConfigureAwait(false);
        var backups = new List<(BackupInfo Info, string Path)>();
        foreach (var row in rows)
        {
            var path = (string)row[2];
            var match = BackupFileName.Match(path);
            if (!match.Success ||
                !Enum.TryParse<BackupKind>(row[1] as string, out var kind) ||
                !Enum.IsDefined(kind))
            {
                continue;
            }

            var backupId = match.Groups["id"].Value;
            backups.Add((
                new()
                {
                    Id = backupId,
                    Name = row[0] as string ?? backupId,
                    CreatedAt = DateTime.ParseExact(
                        backupId,
                        BackupIdFormats,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                    SizeBytes = Convert.ToInt64(row[3], CultureInfo.InvariantCulture),
                    Kind = kind
                },
                path));
        }

        return backups;
    }

    private async Task RestoreAsync(string database, string path, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(
                MasterDatabase,
                RestoreSql,
                [new("@database", database), new("@path", path)],
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await ExecuteAsync(MasterDatabase, MultiUserSql, [new("@database", database)], cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (MagpieException ex)
            {
                logger.LogWarning(ex, "Setting database {Database} to MULTI_USER failed.", database);
            }

            SqlConnection.ClearAllPools();
        }
    }

    private async Task TryDeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(MasterDatabase, DeleteFileSql, [new("@path", path)], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MagpieException ex)
        {
            logger.LogWarning(ex, "Deleting the backup file {BackupPath} failed.", path);
        }
    }

    private async Task<List<object[]>> ExecuteAsync(
        string database,
        string sql,
        SqlParameter[] parameters,
        CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(context.Database.GetConnectionString())
        {
            InitialCatalog = database,
            Pooling = false
        };

        try
        {
            var connection = new SqlConnection(builder.ConnectionString);
            await using var connectionHandle = connection.ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(sql, connection) { CommandTimeout = 0 };
            await using var commandHandle = command.ConfigureAwait(false);
            command.Parameters.AddRange(parameters);

            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using var readerHandle = reader.ConfigureAwait(false);
            var rows = new List<object[]>();
            do
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var row = new object[reader.FieldCount];
                    reader.GetValues(row);
                    rows.Add(row);
                }
            }
            while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));

            return rows;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "The SQL Server database management command failed.");
            throw new MagpieException(ex.Message);
        }
    }

    private sealed record RestoreMarker
    {
        public required string BackupId { get; init; }

        public bool RollbackSaved { get; init; }
    }

    private sealed record OptimizationMarker
    {
        public required DateTime OptimizedAtUtc { get; init; }
    }
}
