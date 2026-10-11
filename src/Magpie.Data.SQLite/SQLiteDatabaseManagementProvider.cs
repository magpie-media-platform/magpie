using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Database;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Enums;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using SQLitePCL;

namespace Magpie.Data.SQLite;

internal sealed class SQLiteDatabaseManagementProvider(
    MagpieDbContext context,
    MagpieHostEnvironment hostEnvironment,
    ISettingsRepository settingsRepository,
    TimeProvider timeProvider,
    ILogger<SQLiteDatabaseManagementProvider> logger) :
        IRecoverableDatabaseManagementProvider,
        IOptimizingDatabaseManagementProvider,
        IStartupValidationDatabaseManagementProvider
{
    private const string SettingsGroupName = "SQLite.Database";
    private const string BackupDirectorySettingKey = "BackupDirectory";
    private const string DefaultBackupDirectory = "backups";
    private const string RestoreMarkerFileName = "restore-pending.json";
    private const string RestoreStagingDirectoryName = "restore-staging";
    private const string StagedDatabaseFileName = "staged.db";
    private const string RollbackDatabaseFileName = "rollback.db";
    private const string RollbackTempSuffix = ".tmp";
    private const string RestoreFailedFileName = "restore-failed.json";
    private const string RestoreAppliedFileName = "restore-applied.json";
    private const string OptimizedMarkerFileName = "db-optimized.json";
    private const int MaxRestoreAttempts = 3;

    private static readonly JsonSerializerOptions MarkerJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] SidecarSuffixes = [WalSuffix, ShmSuffix];

    private static readonly Regex BackupIdFormat = new(
        @"^\d{8}_\d{6}(_\d{3})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string NameSeparator = "__";
    private const string BackupMetadataExtension = ".metadata.json";
    private const string ManualBackupNamePrefix = "Manual ";
    private const string ScheduledBackupNamePrefix = "Scheduled ";
    private const int MaxNameLength = 80;

    private const string WalSuffix = "-wal";
    private const string ShmSuffix = "-shm";
    private const string JournalSuffix = "-journal";

    private bool? _canWriteBackupDirectory;

    public string Provider => "SQLite";

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        return await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DatabaseStartupValidationResult> ValidateStartupAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = context.Database.GetDbConnection();
        var closeConnection = connection.State is not ConnectionState.Open;
        if (closeConnection)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var tableCommand = connection.CreateCommand();

            tableCommand.CommandText =
                """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'table' AND name = '__EFMigrationsLock';
                """;

            var tableCount = await tableCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (tableCount is not long count || count == 0)
            {
                return new(true);
            }

            using var lockCommand = connection.CreateCommand();

            lockCommand.CommandText = "SELECT \"Timestamp\" FROM \"__EFMigrationsLock\" WHERE \"Id\" = 1 LIMIT 1;";

            var lockCreatedAt = await lockCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (lockCreatedAt is null)
            {
                return new(true);
            }

            return new(false, $"Another process is holding the SQLite database lock created at {lockCreatedAt}.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SQLite database lock validation failed.");

            return new(false, $"The SQLite database failed to initialize: {ex.Message}");
        }
        finally
        {
            if (closeConnection)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<IReadOnlyCollection<string>> GetAppliedMigrationsAsync(
        CancellationToken cancellationToken = default)
    {
        var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
        return [.. applied];
    }

    private static string GetDataDirectory(string liveDatabasePath)
    {
        return Path.GetDirectoryName(liveDatabasePath)
            ?? throw new InvalidOperationException("The SQLite database path has no directory.");
    }

    private static string GetMarkerPath(string dataDirectory) =>
        Path.Combine(dataDirectory, RestoreMarkerFileName);

    private static string GetOptimizationMarkerPath(string dataDirectory) =>
        Path.Combine(dataDirectory, OptimizedMarkerFileName);

    private static async Task CopyDatabaseAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Pooling = false
        };
        var destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Pooling = false
        };

        using var sourceConnection = new SqliteConnection(sourceConnectionString.ConnectionString);
        using var destinationConnection = new SqliteConnection(destinationConnectionString.ConnectionString);

        await sourceConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await destinationConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

        sourceConnection.BackupDatabase(destinationConnection);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var tempPath = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                tempPath,
                JsonSerializer.Serialize(value, MarkerJsonOptions),
                cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }

    private static void DeleteRollbackTempFiles(string rollbackPath)
    {
        File.Delete(rollbackPath + RollbackTempSuffix);
        foreach (var suffix in SidecarSuffixes)
        {
            File.Delete(rollbackPath + suffix + RollbackTempSuffix);
        }
    }

    private static void SwapDatabaseIntoPlace(string stagedFilePath, string liveDatabasePath)
    {
        DeleteSidecars(liveDatabasePath);
        File.Copy(stagedFilePath, liveDatabasePath, overwrite: true);
    }

    private static void RollBackDatabase(string rollbackPath, string liveDatabasePath)
    {
        DeleteSidecars(liveDatabasePath);

        if (File.Exists(rollbackPath))
        {
            File.Copy(rollbackPath, liveDatabasePath, overwrite: true);
            foreach (var (live, saved) in EnumerateSidecars(liveDatabasePath, rollbackPath))
            {
                if (File.Exists(saved))
                {
                    File.Copy(saved, live, overwrite: true);
                }
            }
        }
        else if (File.Exists(liveDatabasePath))
        {
            File.Delete(liveDatabasePath);
        }
    }

    private static IEnumerable<(string DatabaseSidecar, string OtherSidecar)> EnumerateSidecars(
        string databasePath,
        string otherPath)
    {
        foreach (var suffix in SidecarSuffixes)
        {
            yield return (databasePath + suffix, otherPath + suffix);
        }
    }

    private static List<string> ReadAppliedMigrations(string databasePath)
    {
        AssertNoSidecars(databasePath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        };
        using var connection = new SqliteConnection(connectionString.ConnectionString);
        connection.Open();
        SqliteUnicodeLower.Register(connection);

        using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory';";
        var historyExists = Convert.ToInt64(existsCommand.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        if (!historyExists)
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\";";

        var applied = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            applied.Add(reader.GetString(0));
        }

        return applied;
    }

    private static void DeleteSidecars(string databasePath)
    {
        foreach (var suffix in SidecarSuffixes)
        {
            File.Delete(databasePath + suffix);
        }
    }

    private static void AssertNoSidecars(string databasePath)
    {
        foreach (var suffix in SidecarSuffixes)
        {
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar))
            {
                throw new InvalidOperationException(
                    $"The database at {databasePath} has an unexpected sidecar {sidecar}.");
            }
        }
    }

    private static void CleanStaging(string dataDirectory)
    {
        var stagingDirectory = Path.Combine(dataDirectory, RestoreStagingDirectoryName);
        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static BackupMetadata CreateFallbackBackupMetadata(string backupId, DateTime createdAtUtc)
    {
        return new BackupMetadata
        {
            BackupId = backupId,
            Kind = BackupKind.Manual,
            CreatedAtUtc = createdAtUtc
        };
    }

    private static string GetBackupMetadataPath(string backupPath)
    {
        return $"{backupPath}{BackupMetadataExtension}";
    }

    private static BackupKind ValidateBackupKind(BackupKind kind)
    {
        if (!Enum.IsDefined(typeof(BackupKind), kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Backup kind is not supported.");
        }

        return kind;
    }

    private static BackupKind NormalizeBackupKind(BackupKind kind)
    {
        return Enum.IsDefined(typeof(BackupKind), kind)
            ? kind
            : BackupKind.Manual;
    }

    private static string GetDefaultBackupName(BackupKind kind, string timestamp)
    {
        return kind switch
        {
            BackupKind.Scheduled => ScheduledBackupNamePrefix + timestamp,
            _ => ManualBackupNamePrefix + timestamp
        };
    }

    private static string SanitizeName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == ' ')
            {
                builder.Append(ch);
            }
        }

        var sanitized = builder.ToString().Trim();

        if (sanitized.Length > MaxNameLength)
        {
            sanitized = sanitized[..MaxNameLength].TrimEnd();
        }

        return sanitized;
    }

    private static (string Id, string Name) ParseBackupFileName(string fileNameWithoutExtension)
    {
        var separatorIndex = fileNameWithoutExtension.IndexOf(NameSeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return (fileNameWithoutExtension, fileNameWithoutExtension);
        }

        var id = fileNameWithoutExtension[..separatorIndex];
        var name = fileNameWithoutExtension[(separatorIndex + NameSeparator.Length)..].Trim();

        return (id, string.IsNullOrEmpty(name) ? id : name);
    }

    private static string? ResolveBackupPath(string backupDirectory, string backupId)
    {
        ValidateBackupId(backupId, nameof(backupId));

        if (!Directory.Exists(backupDirectory))
        {
            return null;
        }

        var exactPath = Path.Combine(backupDirectory, $"{backupId}.db");
        if (File.Exists(exactPath))
        {
            return exactPath;
        }

        var pattern = $"{backupId}{NameSeparator}*.db";
        var match = Directory.GetFiles(backupDirectory, pattern).FirstOrDefault();

        return match;
    }

    private static void ValidateBackupId(string backupId, string parameterName)
    {
        if (string.IsNullOrEmpty(backupId))
        {
            throw new ArgumentException("A backup ID is required.", parameterName);
        }

        if (!BackupIdFormat.IsMatch(backupId))
        {
            throw new ArgumentException(
                $"Backup ID {backupId} is not in the expected format.",
                parameterName);
        }
    }

    public async Task<string> BackupAsync(BackupCreateOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (await GetBackupDeniedReasonAsync(cancellationToken).ConfigureAwait(false) is { } deniedReason)
        {
            throw new MagpieException(deniedReason);
        }

        var backupDirectory = await GetBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var timestamp = now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var kind = ValidateBackupKind(options.Kind);
        var displayName = string.IsNullOrWhiteSpace(options.Name)
            ? GetDefaultBackupName(kind, timestamp)
            : options.Name.Trim();
        var sanitizedName = SanitizeName(displayName);

        var backupId = timestamp;
        if (ResolveBackupPath(backupDirectory, backupId) is not null)
        {
            backupId = $"{timestamp}_{now.Millisecond:D3}";
            if (ResolveBackupPath(backupDirectory, backupId) is not null)
            {
                throw new ArgumentException($"Backup {backupId} already exists.", nameof(options));
            }
        }

        var backupFileName = string.IsNullOrEmpty(sanitizedName)
            ? $"{backupId}.db"
            : $"{backupId}{NameSeparator}{sanitizedName}.db";
        var backupPath = Path.Combine(backupDirectory, backupFileName);
        var backupTempPath = backupPath + ".tmp";
        var metadataPath = GetBackupMetadataPath(backupPath);

        var metadata = new BackupMetadata
        {
            BackupId = backupId,
            Kind = kind,
            CreatedAtUtc = now
        };

        try
        {
            await CopyDatabaseAsync(GetLiveDatabasePath(), backupTempPath, cancellationToken).ConfigureAwait(false);
            await WriteJsonAsync(metadataPath, metadata, cancellationToken).ConfigureAwait(false);
            File.Move(backupTempPath, backupPath);
        }
        catch
        {
            File.Delete(backupTempPath);
            File.Delete(backupTempPath + JournalSuffix);
            File.Delete(metadataPath);
            throw;
        }

        return backupId;
    }

    public async Task<bool> StageRestoreAsync(string backupId, CancellationToken cancellationToken)
    {
        ValidateBackupId(backupId, nameof(backupId));

        var liveDatabasePath = GetLiveDatabasePath();
        var backupDirectory = await GetBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);
        var backupPath = ResolveBackupPath(backupDirectory, backupId);

        if (backupPath is null)
        {
            return false;
        }

        if (await GetRestoreDeniedReasonAsync(cancellationToken).ConfigureAwait(false) is { } deniedReason)
        {
            throw new MagpieException(deniedReason);
        }

        var dataDirectory = GetDataDirectory(liveDatabasePath);
        var stagingDirectory = Path.Combine(dataDirectory, RestoreStagingDirectoryName);

        if (Directory.Exists(stagingDirectory))
        {
            try
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
            catch (IOException ex)
            {
                logger.LogError(
                    ex,
                    "Deleting restore staging directory {StagingDirectory} failed.",
                    stagingDirectory);
                throw new InvalidOperationException(
                    "The provider failed to clear the restore staging directory.",
                    ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError(
                    ex,
                    "Deleting restore staging directory {StagingDirectory} failed.",
                    stagingDirectory);
                throw new InvalidOperationException(
                    "The restore staging directory could not be cleared due to an access error.",
                    ex);
            }
        }

        Directory.CreateDirectory(stagingDirectory);

        var stagedFilePath = Path.Combine(stagingDirectory, StagedDatabaseFileName);

        await CopyDatabaseAsync(backupPath, stagedFilePath, cancellationToken).ConfigureAwait(false);

        var marker = new RestorePendingMarker
        {
            StagedFilePath = stagedFilePath,
            BackupId = backupId,
            RequestedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Attempt = 0
        };

        await WriteJsonAsync(GetMarkerPath(dataDirectory), marker, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Staged backup {BackupId} for restore at next host startup.", backupId);

        return true;
    }

    public async Task<bool> ApplyPendingRestoreAsync(CancellationToken cancellationToken)
    {
        var liveDatabasePath = GetLiveDatabasePath();
        var dataDirectory = GetDataDirectory(liveDatabasePath);
        var markerPath = GetMarkerPath(dataDirectory);

        if (!File.Exists(markerPath))
        {
            return false;
        }

        var marker = await ReadMarkerAsync<RestorePendingMarker>(markerPath, cancellationToken).ConfigureAwait(false);
        if (marker is null)
        {
            logger.LogError(
                "Restore marker {MarkerPath} unusable; restore stopped and marker deleted.",
                markerPath);
            await RecordRestoreFailedAsync(
                dataDirectory,
                backupId: null,
                reason: "The restore marker file could not be parsed.",
                cancellationToken).ConfigureAwait(false);
            ClearMarkerAndStaging(dataDirectory);
            return false;
        }

        if (await IsRestoreAppliedAsync(dataDirectory, marker, cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation(
                "Restore of backup {BackupId} was applied before; clearing its marker and staging.",
                marker.BackupId);
            ClearMarkerAndStaging(dataDirectory);
            return false;
        }

        var stagingDirectory = Path.Combine(dataDirectory, RestoreStagingDirectoryName);
        var rollbackPath = Path.Combine(stagingDirectory, RollbackDatabaseFileName);

        var attempted = marker with { Attempt = marker.Attempt + 1 };
        await WriteJsonAsync(markerPath, attempted, cancellationToken).ConfigureAwait(false);

        if (attempted.Attempt > MaxRestoreAttempts)
        {
            logger.LogError(
                "Restore of backup {BackupId} exceeded {MaxAttempts} attempts; host starts with original database.",
                attempted.BackupId,
                MaxRestoreAttempts);

            if (attempted.SwapCommitted)
            {
                RollBackDatabase(rollbackPath, liveDatabasePath);
            }

            await RecordRestoreFailedAsync(
                dataDirectory,
                attempted.BackupId,
                $"Exceeded the maximum of {MaxRestoreAttempts} restore attempts.",
                cancellationToken).ConfigureAwait(false);
            ClearMarkerAndStaging(dataDirectory);
            return false;
        }

        if (!attempted.SwapCommitted && !File.Exists(attempted.StagedFilePath))
        {
            logger.LogError(
                "Staged database {StagedPath} for restore of backup {BackupId} does not exist; restore stopped.",
                attempted.StagedFilePath,
                attempted.BackupId);
            await RecordRestoreFailedAsync(
                dataDirectory,
                attempted.BackupId,
                "The staged database file was missing.",
                cancellationToken).ConfigureAwait(false);
            ClearMarkerAndStaging(dataDirectory);
            return false;
        }

        if (!attempted.SwapCommitted)
        {
            logger.LogInformation(
                "Applying restore of backup {BackupId} (attempt {Attempt}).",
                attempted.BackupId,
                attempted.Attempt);

            SaveLiveDatabaseForRollback(liveDatabasePath, rollbackPath);
            SwapDatabaseIntoPlace(attempted.StagedFilePath, liveDatabasePath);

            attempted = attempted with { SwapCommitted = true };
            await WriteJsonAsync(markerPath, attempted, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            logger.LogInformation(
                "Continuing restore of backup {BackupId} (attempt {Attempt}); database swap already complete.",
                attempted.BackupId,
                attempted.Attempt);
        }

        try
        {
            ValidateSwappedDatabase(liveDatabasePath);

            logger.LogInformation("Applied restore of backup {BackupId}.", attempted.BackupId);

            await RecordRestoreAppliedAsync(
                dataDirectory,
                attempted.BackupId,
                cancellationToken).ConfigureAwait(false);

            ClearMarkerAndStaging(dataDirectory);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Restore of backup {BackupId} failed validation; rolling back to original database.",
                attempted.BackupId);

            RollBackDatabase(rollbackPath, liveDatabasePath);

            await RecordRestoreFailedAsync(
                dataDirectory,
                attempted.BackupId,
                ex.Message,
                cancellationToken).ConfigureAwait(false);

            ClearMarkerAndStaging(dataDirectory);

            return false;
        }
    }

    public async Task<IEnumerable<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken)
    {
        var backupDirectory = await GetBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);

        if (!Directory.Exists(backupDirectory))
        {
            return [];
        }

        var backups = (
            from path in Directory.GetFiles(backupDirectory, "*.db")
            let backup = CreateBackupInfo(path)
            orderby backup.CreatedAt descending
            select backup).ToList();

        return backups;
    }

    public async Task<bool> DeleteBackupAsync(string backupId, CancellationToken cancellationToken)
    {
        ValidateBackupId(backupId, nameof(backupId));

        var backupDirectory = await GetBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);
        var backupPath = ResolveBackupPath(backupDirectory, backupId);

        if (backupPath is null)
        {
            return false;
        }

        if (await GetDeleteDeniedReasonAsync(cancellationToken).ConfigureAwait(false) is { } deniedReason)
        {
            throw new MagpieException(deniedReason);
        }

        File.Delete(backupPath);
        File.Delete(GetBackupMetadataPath(backupPath));

        return true;
    }

    public async Task<string?> GetBackupFilePathAsync(string backupId, CancellationToken cancellationToken)
    {
        ValidateBackupId(backupId, nameof(backupId));

        var backupDirectory = await GetBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);

        return ResolveBackupPath(backupDirectory, backupId);
    }

    public Task<string?> GetBackupDeniedReasonAsync(CancellationToken cancellationToken)
    {
        return GetBackupDirectoryDeniedReasonAsync("Create", cancellationToken);
    }

    public async Task<string?> GetRestoreDeniedReasonAsync(CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(CreatePoolingDisabledConnectionString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        return raw.sqlite3_db_readonly(connection.Handle, "main") == 0
            ? null
            : "Restore needs write access to the database file";
    }

    public Task<string?> GetDeleteDeniedReasonAsync(CancellationToken cancellationToken)
    {
        return GetBackupDirectoryDeniedReasonAsync("Delete", cancellationToken);
    }

    public async Task<DateTime?> GetLastOptimizedAsync(CancellationToken cancellationToken)
    {
        var dataDirectory = GetDataDirectory(GetLiveDatabasePath());
        var markerPath = GetOptimizationMarkerPath(dataDirectory);
        if (!File.Exists(markerPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false);
            var marker = JsonSerializer.Deserialize<OptimizationMarker>(json, MarkerJsonOptions);
            return marker is null || marker.OptimizedAtUtc == default ? null : marker.OptimizedAtUtc;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Reading optimization marker {MarkerPath} failed; reporting no optimization date.",
                markerPath);
            return null;
        }
    }

    public async Task OptimizeAsync(CancellationToken cancellationToken)
    {
        var liveDatabasePath = GetLiveDatabasePath();
        var connectionString = CreatePoolingDisabledConnectionString();

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        SqliteUnicodeLower.Register(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM; PRAGMA wal_checkpoint(TRUNCATE);";

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var marker = new OptimizationMarker
        {
            OptimizedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };
        await WriteJsonAsync(
            GetOptimizationMarkerPath(GetDataDirectory(liveDatabasePath)),
            marker,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordRestoreFailedAsync(
        string dataDirectory,
        string? backupId,
        string reason,
        CancellationToken cancellationToken)
    {
        var marker = new RestoreFailedMarker
        {
            BackupId = backupId,
            Reason = reason,
            FailedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };
        await WriteJsonAsync(
            Path.Combine(dataDirectory, RestoreFailedFileName),
            marker,
            cancellationToken).ConfigureAwait(false);
    }

    private void ClearMarkerAndStaging(string dataDirectory)
    {
        try
        {
            File.Delete(GetMarkerPath(dataDirectory));
            CleanStaging(dataDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Clearing the restore marker and staging in {DataDirectory} failed.", dataDirectory);
        }
    }

    private string GetLiveDatabasePath()
    {
        var connectionString = context.Database.GetDbConnection().ConnectionString
            ?? throw new InvalidOperationException("No SQLite connection string is configured.");
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            throw new InvalidOperationException("The SQLite connection string has no data source.");
        }

        return Path.GetFullPath(dataSource);
    }

    private string CreatePoolingDisabledConnectionString()
    {
        var connectionString = context.Database.GetDbConnection().ConnectionString
            ?? throw new InvalidOperationException("No SQLite connection string is configured.");
        var builder = new SqliteConnectionStringBuilder(connectionString)
        {
            Pooling = false
        };

        return builder.ConnectionString;
    }

    private async Task<bool> IsRestoreAppliedAsync(
        string dataDirectory,
        RestorePendingMarker marker,
        CancellationToken cancellationToken)
    {
        var appliedPath = Path.Combine(dataDirectory, RestoreAppliedFileName);
        if (!File.Exists(appliedPath))
        {
            return false;
        }

        var applied = await ReadMarkerAsync<RestoreAppliedMarker>(appliedPath, cancellationToken)
            .ConfigureAwait(false);

        return applied is not null &&
            applied.BackupId == marker.BackupId &&
            applied.AppliedAtUtc >= marker.RequestedAtUtc;
    }

    private async Task<TMarker?> ReadMarkerAsync<TMarker>(string markerPath, CancellationToken cancellationToken)
        where TMarker : class
    {
        try
        {
            var json = await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<TMarker>(json, MarkerJsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Reading restore marker {MarkerPath} failed.", markerPath);
            return null;
        }
    }

    private void SaveLiveDatabaseForRollback(string liveDatabasePath, string rollbackPath)
    {
        if (File.Exists(rollbackPath))
        {
            return;
        }

        if (!File.Exists(liveDatabasePath))
        {
            logger.LogInformation(
                "No database at {LiveDatabasePath}; restore continues without rollback copy.",
                liveDatabasePath);
            return;
        }

        DeleteRollbackTempFiles(rollbackPath);

        var rollbackTempPath = rollbackPath + RollbackTempSuffix;
        File.Copy(liveDatabasePath, rollbackTempPath, overwrite: true);

        foreach (var (live, saved) in EnumerateSidecars(liveDatabasePath, rollbackPath))
        {
            if (File.Exists(live))
            {
                var savedTempPath = saved + RollbackTempSuffix;
                File.Copy(live, savedTempPath, overwrite: true);
                File.Move(savedTempPath, saved, overwrite: true);
            }
        }

        File.Move(rollbackTempPath, rollbackPath, overwrite: true);
    }

    private void ValidateSwappedDatabase(string liveDatabasePath)
    {
        var appliedMigrations = ReadAppliedMigrations(liveDatabasePath);

        if (appliedMigrations.Count == 0)
        {
            return;
        }

        var knownMigrations = context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        var unknown = appliedMigrations.Where(m => !knownMigrations.Contains(m)).ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"The restored database contains unknown migrations: {string.Join(", ", unknown)}.");
        }
    }

    private async Task RecordRestoreAppliedAsync(
        string dataDirectory,
        string? backupId,
        CancellationToken cancellationToken)
    {
        var appliedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var marker = new RestoreAppliedMarker
        {
            BackupId = backupId,
            AppliedAtUtc = appliedAtUtc
        };
        await WriteJsonAsync(
            Path.Combine(dataDirectory, RestoreAppliedFileName),
            marker,
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Recorded restore-applied marker for backup {BackupId} at {AppliedAtUtc}.",
            backupId,
            appliedAtUtc);
    }

    private async Task<string?> GetBackupDirectoryDeniedReasonAsync(string action, CancellationToken cancellationToken)
    {
        if (_canWriteBackupDirectory is null)
        {
            var backupDirectory = await GetBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(backupDirectory);
                await File.Create(Path.Combine(backupDirectory, Path.GetRandomFileName()), 1, FileOptions.DeleteOnClose)
                    .DisposeAsync()
                    .ConfigureAwait(false);
                _canWriteBackupDirectory = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Writing to the backup directory {BackupDirectory} failed.", backupDirectory);
                _canWriteBackupDirectory = false;
            }
        }

        return _canWriteBackupDirectory.Value ? null : $"{action} needs write access to the backup directory";
    }

    private async Task<string> GetBackupDirectoryAsync(CancellationToken cancellationToken)
    {
        var settingsGroup = await settingsRepository.GetByIdAsync(
            new SettingsGroupLookupKey(null, SettingsGroupName),
            cancellationToken).ConfigureAwait(false);

        var backupFolder = settingsGroup?.Settings
            .FirstOrDefault(s => s.Key == BackupDirectorySettingKey)?.Value
            ?? DefaultBackupDirectory;

        if (!Path.IsPathRooted(backupFolder))
        {
            backupFolder = Path.Combine(hostEnvironment.DataRoot, backupFolder);
        }

        return backupFolder;
    }

    private BackupInfo CreateBackupInfo(string path)
    {
        var parsed = ParseBackupFileName(Path.GetFileNameWithoutExtension(path));
        var info = new FileInfo(path);
        var metadata = ReadBackupMetadata(path, parsed.Id, info.CreationTimeUtc);

        return new BackupInfo
        {
            Id = parsed.Id,
            Name = parsed.Name,
            CreatedAt = metadata.CreatedAtUtc,
            SizeBytes = info.Length,
            Kind = metadata.Kind
        };
    }

    private BackupMetadata ReadBackupMetadata(
        string backupPath,
        string backupId,
        DateTime fallbackCreatedAtUtc)
    {
        var metadataPath = GetBackupMetadataPath(backupPath);
        if (!File.Exists(metadataPath))
        {
            return CreateFallbackBackupMetadata(backupId, fallbackCreatedAtUtc);
        }

        try
        {
            var json = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize<BackupMetadata>(json, MarkerJsonOptions);
            if (metadata is null ||
                !string.Equals(metadata.BackupId, backupId, StringComparison.Ordinal) ||
                metadata.CreatedAtUtc == default)
            {
                logger.LogWarning(
                    "Backup metadata {MetadataPath} is invalid; marking backup {BackupId} as manual.",
                    metadataPath,
                    backupId);
                return CreateFallbackBackupMetadata(backupId, fallbackCreatedAtUtc);
            }

            return metadata with
            {
                Kind = NormalizeBackupKind(metadata.Kind)
            };
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Reading backup metadata {MetadataPath} failed; marking backup {BackupId} as manual.",
                metadataPath,
                backupId);
            return CreateFallbackBackupMetadata(backupId, fallbackCreatedAtUtc);
        }
    }

    private sealed record RestoreAppliedMarker
    {
        public string? BackupId { get; init; }

        public required DateTime AppliedAtUtc { get; init; }
    }

    private sealed record RestoreFailedMarker
    {
        public string? BackupId { get; init; }

        public required string Reason { get; init; }

        public required DateTime FailedAtUtc { get; init; }
    }

    private sealed record RestorePendingMarker
    {
        public required string StagedFilePath { get; init; }

        public required string BackupId { get; init; }

        public required DateTime RequestedAtUtc { get; init; }

        public int Attempt { get; init; }

        public bool SwapCommitted { get; init; }
    }

    private sealed record BackupMetadata
    {
        public required string BackupId { get; init; }

        public BackupKind Kind { get; init; } = BackupKind.Manual;

        public required DateTime CreatedAtUtc { get; init; }
    }

    private sealed record OptimizationMarker
    {
        public required DateTime OptimizedAtUtc { get; init; }
    }
}
