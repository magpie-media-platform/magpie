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

using Magpie.Abstractions.Database;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Enums;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Data.SQLite;

/// <summary>
/// The SQLite implementation of the database management provider.
/// </summary>
public sealed class SQLiteDatabaseManagementProvider(
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

    /// <inheritdoc />
    public string Provider => "SQLite";

    /// <inheritdoc />
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        return await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
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
            logger.LogError(ex, "The provider failed to validate the SQLite database lock. The validation will fail.");

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

    /// <inheritdoc />
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

    private static async Task WriteMarkerAsync(
        string markerPath,
        RestorePendingMarker marker,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(marker, MarkerJsonOptions);
        var tempPath = markerPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, markerPath, overwrite: true);
    }

    private static void DeleteRollbackTempFiles(string rollbackPath)
    {
        var mainTempPath = rollbackPath + RollbackTempSuffix;
        if (File.Exists(mainTempPath))
        {
            File.Delete(mainTempPath);
        }

        foreach (var suffix in SidecarSuffixes)
        {
            var sidecarTempPath = rollbackPath + suffix + RollbackTempSuffix;
            if (File.Exists(sidecarTempPath))
            {
                File.Delete(sidecarTempPath);
            }
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
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
            }
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
                    $"The database at {databasePath} has an unexpected sidecar {sidecar}. Staged " +
                    "restore databases must be sidecar-free before being opened directly.");
            }
        }
    }

    private static async Task RecordOptimizedAsync(
        string dataDirectory,
        DateTime optimizedAtUtc,
        CancellationToken cancellationToken)
    {
        var marker = new OptimizationMarker
        {
            OptimizedAtUtc = optimizedAtUtc
        };
        var json = JsonSerializer.Serialize(marker, MarkerJsonOptions);
        var path = GetOptimizationMarkerPath(dataDirectory);
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }

    private static void CleanStaging(string dataDirectory)
    {
        var stagingDirectory = Path.Combine(dataDirectory, RestoreStagingDirectoryName);
        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static void ClearMarkerAndStaging(string dataDirectory)
    {
        var markerPath = GetMarkerPath(dataDirectory);
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }

        CleanStaging(dataDirectory);
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

    private static async Task WriteBackupMetadataAsync(
        string backupPath,
        BackupMetadata metadata,
        CancellationToken cancellationToken)
    {
        var metadataPath = GetBackupMetadataPath(backupPath);
        var temporaryPath = $"{metadataPath}.tmp";
        var json = JsonSerializer.Serialize(metadata, MarkerJsonOptions);

        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, metadataPath, overwrite: true);
    }

    private static void DeleteBackupMetadata(string backupPath)
    {
        var metadataPath = GetBackupMetadataPath(backupPath);
        if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }
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

    /// <inheritdoc />
    public async Task<string> BackupAsync(BackupCreateOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var connectionString = CreatePoolingDisabledConnectionString();
        var backupDirectory = await GetBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(backupDirectory);

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

        var backupConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Pooling = false
        };

        using (var sourceConnection = new SqliteConnection(connectionString))
        using (var backupConnection = new SqliteConnection(backupConnectionString.ConnectionString))
        {
            await sourceConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await backupConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            sourceConnection.BackupDatabase(backupConnection);
        }

        var metadata = new BackupMetadata
        {
            BackupId = backupId,
            Kind = kind,
            CreatedAtUtc = now
        };

        await WriteBackupMetadataAsync(backupPath, metadata, cancellationToken).ConfigureAwait(false);

        return backupId;
    }

    /// <inheritdoc />
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
                    "The provider failed to delete the restore staging directory {StagingDirectory}. " +
                    "The delete operation raised an I/O error, so the provider stopped the restore request.",
                    stagingDirectory);
                throw new InvalidOperationException(
                    "The provider failed to clear the restore staging directory. A file can be locked. " +
                    "Release the file lock, and then retry the restore.",
                    ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError(
                    ex,
                    "The provider failed to delete the restore staging directory {StagingDirectory}. " +
                    "The operating system denied access, so the provider stopped the restore request.",
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
            RequestedBy = null,
            Attempt = 0
        };

        await WriteMarkerAsync(GetMarkerPath(dataDirectory), marker, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("The provider staged backup {BackupId} for restore at the next host startup.", backupId);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ApplyPendingRestoreAsync(CancellationToken cancellationToken)
    {
        var liveDatabasePath = GetLiveDatabasePath();
        var dataDirectory = GetDataDirectory(liveDatabasePath);
        var markerPath = GetMarkerPath(dataDirectory);

        if (!File.Exists(markerPath))
        {
            return false;
        }

        var marker = await ReadMarkerAsync(markerPath, cancellationToken).ConfigureAwait(false);
        if (marker is null)
        {
            logger.LogError(
                "The restore marker at {MarkerPath} was not usable, so the provider stopped the " +
                "restore and deleted the marker. No retry will happen.",
                markerPath);
            await RecordRestoreFailedAsync(
                dataDirectory,
                backupId: null,
                reason: "The restore marker file could not be parsed.",
                cancellationToken).ConfigureAwait(false);
            ClearMarkerAndStaging(dataDirectory);
            return false;
        }

        var stagingDirectory = Path.Combine(dataDirectory, RestoreStagingDirectoryName);
        var rollbackPath = Path.Combine(stagingDirectory, RollbackDatabaseFileName);

        var attempted = marker with { Attempt = marker.Attempt + 1 };
        await WriteMarkerAsync(markerPath, attempted, cancellationToken).ConfigureAwait(false);

        if (attempted.Attempt > MaxRestoreAttempts)
        {
            logger.LogError(
                "The restore of backup {BackupId} exceeded {MaxAttempts} attempts. " +
                "The host will start with the original database.",
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
                "The staged database {StagedPath} for the restore of backup {BackupId} does not exist. " +
                "The restore will stop.",
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
            logger.LogWarning(
                "The provider will apply the restore of backup {BackupId} (attempt {Attempt}) and will " +
                "swap the database.",
                attempted.BackupId,
                attempted.Attempt);

            SaveLiveDatabaseForRollback(liveDatabasePath, rollbackPath);
            SwapDatabaseIntoPlace(attempted.StagedFilePath, liveDatabasePath);

            attempted = attempted with { SwapCommitted = true };
            await WriteMarkerAsync(markerPath, attempted, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            logger.LogWarning(
                "The provider will continue the restore of backup {BackupId} (attempt {Attempt}). The " +
                "database swap is already complete, so the provider will validate the live database.",
                attempted.BackupId,
                attempted.Attempt);
        }

        try
        {
            ValidateSwappedDatabase(liveDatabasePath);

            logger.LogInformation("The provider applied the restore of backup {BackupId}.", attempted.BackupId);

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
                "The restore of backup {BackupId} failed validation. The provider will roll back to the " +
                "original database.",
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<bool> DeleteBackupAsync(string backupId, CancellationToken cancellationToken)
    {
        ValidateBackupId(backupId, nameof(backupId));

        var backupDirectory = await GetBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);
        var backupPath = ResolveBackupPath(backupDirectory, backupId);

        if (backupPath is null)
        {
            return false;
        }

        File.Delete(backupPath);
        DeleteBackupMetadata(backupPath);

        return true;
    }

    /// <inheritdoc />
    public async Task<string?> GetBackupFilePathAsync(string backupId, CancellationToken cancellationToken)
    {
        ValidateBackupId(backupId, nameof(backupId));

        var backupDirectory = await GetBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);

        return ResolveBackupPath(backupDirectory, backupId);
    }

    /// <inheritdoc />
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "The provider failed to read the optimization marker {MarkerPath}, " +
                "so the provider reported no optimization date.", markerPath);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task OptimizeAsync(CancellationToken cancellationToken)
    {
        var liveDatabasePath = GetLiveDatabasePath();
        var connectionString = CreatePoolingDisabledConnectionString();

        using var connection = new SqliteConnection(connectionString);

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM; PRAGMA wal_checkpoint(TRUNCATE);";

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await RecordOptimizedAsync(
            GetDataDirectory(liveDatabasePath),
            timeProvider.GetUtcNow().UtcDateTime,
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
        var json = JsonSerializer.Serialize(marker, MarkerJsonOptions);
        var path = Path.Combine(dataDirectory, RestoreFailedFileName);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
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

    private async Task<RestorePendingMarker?> ReadMarkerAsync(
        string markerPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<RestorePendingMarker>(json, MarkerJsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "The provider failed to parse the restore marker at {MarkerPath}.", markerPath);
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
                "No database exists at {LiveDatabasePath}. The restore will continue without a rollback copy.",
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
                $"The restored database contains unknown migrations: {string.Join(", ", unknown)}. " +
                "The database requires a newer Magpie version.");
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
        var json = JsonSerializer.Serialize(marker, MarkerJsonOptions);
        var path = Path.Combine(dataDirectory, RestoreAppliedFileName);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "The provider recorded the restore-applied marker for backup {BackupId} at {AppliedAtUtc}.",
            backupId,
            appliedAtUtc);
    }

    private async Task<string> GetBackupDirectoryAsync(CancellationToken cancellationToken)
    {
        var settingsGroup = await settingsRepository.GetByIdAsync(
            new SettingsGroupLookupKey(null, SettingsGroupName),
            cancellationToken).ConfigureAwait(false);

        var settings = settingsGroup?.Settings.ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal)

            ?? new Dictionary<string, string>();
        var backupFolder = settings.TryGetValue(BackupDirectorySettingKey, out var value)
            ? value
            : DefaultBackupDirectory;

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
                    "The backup metadata {MetadataPath} is invalid. The provider will mark backup " +
                    "{BackupId} as manual.",
                    metadataPath,
                    backupId);
                return CreateFallbackBackupMetadata(backupId, fallbackCreatedAtUtc);
            }

            return metadata with
            {
                Kind = NormalizeBackupKind(metadata.Kind)
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(
                ex,
                "The provider cannot read the backup metadata {MetadataPath}. The provider will mark " +
                "backup {BackupId} as manual.",
                metadataPath,
                backupId);
            return CreateFallbackBackupMetadata(backupId, fallbackCreatedAtUtc);
        }
    }

    private sealed record RestoreAppliedMarker
    {
        [JsonPropertyName("backupId")]
        public string? BackupId { get; init; }

        [JsonPropertyName("appliedAtUtc")]
        public required DateTime AppliedAtUtc { get; init; }
    }

    private sealed record RestoreFailedMarker
    {
        [JsonPropertyName("backupId")]
        public string? BackupId { get; init; }

        [JsonPropertyName("reason")]
        public required string Reason { get; init; }

        [JsonPropertyName("failedAtUtc")]
        public required DateTime FailedAtUtc { get; init; }
    }

    private sealed record RestorePendingMarker
    {
        [JsonPropertyName("stagedFilePath")]
        public required string StagedFilePath { get; init; }

        [JsonPropertyName("backupId")]
        public required string BackupId { get; init; }

        [JsonPropertyName("requestedAtUtc")]
        public required DateTime RequestedAtUtc { get; init; }

        [JsonPropertyName("requestedBy")]
        public Guid? RequestedBy { get; init; }

        [JsonPropertyName("attempt")]
        public int Attempt { get; init; }

        [JsonPropertyName("swapCommitted")]
        public bool SwapCommitted { get; init; }
    }

    private sealed record BackupMetadata
    {
        [JsonPropertyName("backupId")]
        public required string BackupId { get; init; }

        [JsonPropertyName("kind")]
        public BackupKind Kind { get; init; } = BackupKind.Manual;

        [JsonPropertyName("createdAtUtc")]
        public required DateTime CreatedAtUtc { get; init; }
    }

    private sealed record OptimizationMarker
    {
        [JsonPropertyName("optimizedAtUtc")]
        public required DateTime OptimizedAtUtc { get; init; }
    }
}
