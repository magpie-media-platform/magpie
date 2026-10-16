using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace Magpie.Data.PostgreSQL;

internal sealed partial class PostgreSqlDatabaseManagementProvider(
    MagpieDbContext context,
    MagpieHostEnvironment hostEnvironment,
    TimeProvider timeProvider,
    PostgreSqlToolOptions toolOptions,
    ILogger<PostgreSqlDatabaseManagementProvider> logger) :
        IRecoverableDatabaseManagementProvider,
        IOptimizingDatabaseManagementProvider
{
    private const string MaintenanceDatabase = "postgres";
    private const string BackupDirectoryName = "backups";
    private const string BackupExtension = ".dump";
    private const string MetadataExtension = ".json";
    private const string RestoreMarkerFileName = "restore-pending.json";
    private const string RestoreDatabaseSuffix = "_restore";
    private const string PreviousDatabaseSuffix = "_previous";
    private const string BackupIdFormat = "yyyyMMdd_HHmmss";
    private const string DumpTool = "pg_dump";
    private const string RestoreTool = "pg_restore";

    private const UnixFileMode ExecuteModes =
        UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    private static readonly TimeSpan ToolVersionTimeout = TimeSpan.FromSeconds(5);

    private const string RestorePrivilegesSql =
        """
        SELECT r.rolcreatedb OR r.rolsuper, pg_has_role(d.datdba, 'USAGE')
        FROM pg_roles AS r, pg_database AS d
        WHERE r.rolname = current_user AND d.datname = current_database();
        """;

    private const string CreateRestoreDatabaseSql =
        """
        SELECT format(
            'CREATE DATABASE %I WITH TEMPLATE template0 OWNER %I ENCODING %L LC_COLLATE %L LC_CTYPE %L',
            @restoreDatabase,
            pg_get_userbyid(datdba),
            pg_encoding_to_char(encoding),
            datcollate,
            datctype)
        FROM pg_database
        WHERE datname = @database;
        """;

    private const string AppliedMigrationsSql = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"";

    private const string OptimizeSql = "VACUUM (ANALYZE)";

    private const string LastOptimizedSql = "SELECT max(last_vacuum) FROM pg_stat_user_tables";

    private static readonly string[] BackupIdFormats = [BackupIdFormat, BackupIdFormat + "_fff"];

    public string Provider => "PostgreSQL";

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

    public async Task<IReadOnlyCollection<string>> GetUnknownMigrationsAsync(
        CancellationToken cancellationToken = default)
    {
        var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
        return [.. applied.Except(context.Database.GetMigrations(), StringComparer.Ordinal)];
    }

    public async Task<string> BackupAsync(BackupCreateOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (await GetBackupDeniedReasonAsync(cancellationToken).ConfigureAwait(false) is { } deniedReason)
        {
            throw new MagpieException(deniedReason);
        }

        var backupDirectory = Path.Combine(hostEnvironment.DataRoot, BackupDirectoryName);
        Directory.CreateDirectory(backupDirectory);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var timestamp = now.ToString(BackupIdFormat, CultureInfo.InvariantCulture);
        var backupId = File.Exists(Path.Combine(backupDirectory, timestamp + BackupExtension))
            ? $"{timestamp}_{now.Millisecond:D3}"
            : timestamp;
        var backupPath = Path.Combine(backupDirectory, backupId + BackupExtension);
        var backupTempPath = backupPath + ".tmp";
        var metadataPath = Path.Combine(backupDirectory, backupId + MetadataExtension);
        var metadata = new BackupMetadata
        {
            Name = string.IsNullOrWhiteSpace(options.Name) ? $"{options.Kind} {timestamp}" : options.Name.Trim(),
            Kind = options.Kind
        };

        try
        {
            await RunToolAsync(
                DumpTool,
                ["--format=custom", $"--file={backupTempPath}", $"--dbname={GetDatabaseName()}"],
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata), cancellationToken)
                .ConfigureAwait(false);
            File.Move(backupTempPath, backupPath);
        }
        catch
        {
            File.Delete(backupTempPath);
            File.Delete(metadataPath);
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

        await RunToolAsync(RestoreTool, ["--list", path], cancellationToken).ConfigureAwait(false);
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

        var database = GetDatabaseName();
        var restoreDatabase = database + RestoreDatabaseSuffix;
        var previousDatabase = database + PreviousDatabaseSuffix;

        logger.LogInformation("Applying restore of backup {BackupId}.", marker.BackupId);
        NpgsqlConnection.ClearAllPools();

        try
        {
            await DropDatabaseAsync(restoreDatabase, cancellationToken).ConfigureAwait(false);
            await DropDatabaseAsync(previousDatabase, cancellationToken).ConfigureAwait(false);
            var createSql = (string)(await ExecuteAsync(
                MaintenanceDatabase,
                CreateRestoreDatabaseSql,
                [
                    new("restoreDatabase", restoreDatabase),
                    new("database", database)
                ],
                cancellationToken).ConfigureAwait(false))[0][0];
            await ExecuteAsync(MaintenanceDatabase, createSql, [], cancellationToken).ConfigureAwait(false);
            await RunToolAsync(
                RestoreTool,
                ["--no-owner", "--exit-on-error", $"--dbname={restoreDatabase}", path],
                cancellationToken).ConfigureAwait(false);

            var knownMigrations = context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
            var unknown = (await ExecuteAsync(restoreDatabase, AppliedMigrationsSql, [], cancellationToken)
                    .ConfigureAwait(false))
                .Select(row => (string)row[0])
                .Where(migration => !knownMigrations.Contains(migration))
                .ToArray();
            if (unknown.Length > 0)
            {
                throw new InvalidOperationException(
                    $"The restored database contains unknown migrations: {string.Join(", ", unknown)}.");
            }

            await ExecuteAsync(
                MaintenanceDatabase,
                $"""
                BEGIN;
                SELECT pg_terminate_backend(pid) FROM pg_stat_activity
                WHERE datname = @database AND pid <> pg_backend_pid();
                ALTER DATABASE {Quote(database)} RENAME TO {Quote(previousDatabase)};
                ALTER DATABASE {Quote(restoreDatabase)} RENAME TO {Quote(database)};
                COMMIT;
                """,
                [new("database", database)],
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Restore of backup {BackupId} failed; the original database is unchanged.",
                marker.BackupId);
            File.Delete(markerPath);
            await DropDatabaseAsync(restoreDatabase, cancellationToken).ConfigureAwait(false);
            return false;
        }

        File.Delete(markerPath);
        await DropDatabaseAsync(previousDatabase, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Applied restore of backup {BackupId}.", marker.BackupId);

        return true;
    }

    public async Task<IEnumerable<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        var backupDirectory = Path.Combine(hostEnvironment.DataRoot, BackupDirectoryName);
        if (!Directory.Exists(backupDirectory))
        {
            return [];
        }

        var backups = new List<BackupInfo>();
        foreach (var path in Directory.GetFiles(backupDirectory, "*" + BackupExtension))
        {
            var backupId = Path.GetFileNameWithoutExtension(path);
            if (!TryParseBackupId(backupId, out var createdAt))
            {
                continue;
            }

            var metadata = await ReadJsonAsync<BackupMetadata>(
                Path.Combine(backupDirectory, backupId + MetadataExtension),
                cancellationToken).ConfigureAwait(false);
            backups.Add(new()
            {
                Id = backupId,
                Name = metadata?.Name ?? backupId,
                CreatedAt = createdAt,
                SizeBytes = new FileInfo(path).Length,
                Kind = metadata?.Kind ?? BackupKind.Manual
            });
        }

        return [.. backups.OrderByDescending(backup => backup.CreatedAt)];
    }

    public async Task<bool> DeleteBackupAsync(string backupId, CancellationToken cancellationToken = default)
    {
        var path = await GetBackupFilePathAsync(backupId, cancellationToken).ConfigureAwait(false);
        if (path is null)
        {
            return false;
        }

        File.Delete(path);
        File.Delete(Path.ChangeExtension(path, MetadataExtension));

        return true;
    }

    public Task<string?> GetBackupFilePathAsync(string backupId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(hostEnvironment.DataRoot, BackupDirectoryName, backupId + BackupExtension);

        return Task.FromResult(TryParseBackupId(backupId, out _) && File.Exists(path) ? path : null);
    }

    public async Task<string?> GetBackupDeniedReasonAsync(CancellationToken cancellationToken = default)
    {
        return await GetToolDeniedReasonAsync(DumpTool, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetRestoreDeniedReasonAsync(CancellationToken cancellationToken = default)
    {
        var privileges = (await ExecuteAsync(GetDatabaseName(), RestorePrivilegesSql, [], cancellationToken)
            .ConfigureAwait(false))[0];
        if (!(bool)privileges[0])
        {
            return "Restore needs the CREATEDB privilege on PostgreSQL";
        }

        if (!(bool)privileges[1])
        {
            return "Restore needs the database owner role on PostgreSQL";
        }

        return await GetToolDeniedReasonAsync(RestoreTool, cancellationToken).ConfigureAwait(false);
    }

    public Task<string?> GetDeleteDeniedReasonAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    public async Task<DateTime?> GetLastOptimizedAsync(CancellationToken cancellationToken = default)
    {
        var rows = await ExecuteAsync(GetDatabaseName(), LastOptimizedSql, [], cancellationToken).ConfigureAwait(false);

        return rows[0][0] as DateTime?;
    }

    public async Task OptimizeAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(GetDatabaseName(), OptimizeSql, [], cancellationToken).ConfigureAwait(false);
    }

    private static bool TryParseBackupId(string backupId, out DateTime createdAt)
    {
        return DateTime.TryParseExact(
            backupId,
            BackupIdFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out createdAt);
    }

    private static string Quote(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static bool IsExecutable(string path)
    {
        return File.Exists(path) &&
            (OperatingSystem.IsWindows() || (File.GetUnixFileMode(path) & ExecuteModes) != 0);
    }

    private static async Task<int?> GetToolMajorVersionAsync(string path, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(path, "--version")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var exit = process.WaitForExitAsync(cancellationToken);
        if (await Task.WhenAny(exit, Task.Delay(ToolVersionTimeout, CancellationToken.None)).ConfigureAwait(false) !=
            exit)
        {
            process.Kill(entireProcessTree: true);
            return null;
        }

        await exit.ConfigureAwait(false);
        var match = ToolVersionPattern().Match(await output.ConfigureAwait(false));

        return process.ExitCode == 0 && match.Success
            ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
            : null;
    }

    [GeneratedRegex(@"\)\s+(\d+)")]
    private static partial Regex ToolVersionPattern();

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
        var database = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()).Database;

        return string.IsNullOrEmpty(database)
            ? throw new InvalidOperationException("The PostgreSQL connection string has no database.")
            : database;
    }

    private string? FindTool(string tool)
    {
        var fileName = OperatingSystem.IsWindows() ? tool + ".exe" : tool;

        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Prepend(toolOptions.Directory)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.Combine(directory!, fileName))
            .FirstOrDefault(IsExecutable);
    }

    private async Task<string?> GetToolDeniedReasonAsync(string tool, CancellationToken cancellationToken)
    {
        var path = FindTool(tool);
        if (path is null)
        {
            return $"{tool} was not found on PATH or in the PostgreSQL tools directory";
        }

        var version = await GetToolMajorVersionAsync(path, cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            return $"The version of {tool} at {path} cannot be read";
        }

        var serverVersion = await GetServerMajorVersionAsync(cancellationToken).ConfigureAwait(false);

        return version < serverVersion
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{tool} is version {version} but the PostgreSQL server is version {serverVersion}")
            : null;
    }

    private async Task<int> GetServerMajorVersionAsync(CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return ((NpgsqlConnection)context.Database.GetDbConnection()).PostgreSqlVersion.Major;
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private async Task DropDatabaseAsync(string database, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(
                MaintenanceDatabase,
                $"DROP DATABASE IF EXISTS {Quote(database)} WITH (FORCE)",
                [],
                cancellationToken).ConfigureAwait(false);
        }
        catch (MagpieException ex)
        {
            logger.LogWarning(ex, "Dropping database {Database} failed.", database);
        }
    }

    private async Task<List<object[]>> ExecuteAsync(
        string database,
        string sql,
        NpgsqlParameter[] parameters,
        CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString())
        {
            Database = database,
            Pooling = false
        };

        try
        {
            var connection = new NpgsqlConnection(builder.ConnectionString);
            await using var connectionHandle = connection.ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
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
        catch (NpgsqlException ex)
        {
            logger.LogError(ex, "The PostgreSQL database management command failed.");
            throw new MagpieException(ex.Message);
        }
    }

    private async Task RunToolAsync(string tool, string[] arguments, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString());
        var startInfo = new ProcessStartInfo(FindTool(tool) ?? tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["PGHOST"] = builder.Host;
        startInfo.Environment["PGPORT"] = builder.Port.ToString(CultureInfo.InvariantCulture);
        startInfo.Environment["PGUSER"] = builder.Username;
        startInfo.Environment["PGPASSWORD"] = builder.Password;
        startInfo.Environment["PGSSLMODE"] = builder.SslMode switch
        {
            SslMode.VerifyCA => "verify-ca",
            SslMode.VerifyFull => "verify-full",
            var mode => mode.ToString().ToLowerInvariant()
        };

        Process process;
        try
        {
            process = Process.Start(startInfo)!;
        }
        catch (Win32Exception ex)
        {
            logger.LogError(ex, "Starting the PostgreSQL tool {Tool} failed.", tool);
            throw new MagpieException($"The PostgreSQL tool {tool} was not found.");
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                process.Kill(entireProcessTree: true);
                await Task.WhenAll(new Task[] { output, error })
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                throw;
            }

            await output.ConfigureAwait(false);
            var errorText = await error.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new MagpieException(
                    $"The PostgreSQL tool {tool} failed with exit code {process.ExitCode}: {errorText.Trim()}");
            }
        }
    }

    private sealed record BackupMetadata
    {
        public required string Name { get; init; }

        public BackupKind Kind { get; init; }
    }

    private sealed record RestoreMarker
    {
        public required string BackupId { get; init; }
    }
}
