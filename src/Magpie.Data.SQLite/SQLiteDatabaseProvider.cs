using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Configuration;
using Magpie.Abstractions.Database;
using Magpie.Abstractions.System;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using static Magpie.Abstractions.Database.IDatabaseManagementProvider;

namespace Magpie.Data.SQLite;

/// <summary>
/// SQLite database management provider with backup, restore, and vacuum operations.
/// </summary>
/// <param name="context">The database context.</param>
/// <param name="magpieSystem">The magpie system.</param>
/// <param name="systemSettings">The system settings provider.</param>
public sealed class SQLiteDatabaseManagementProvider(
    MagpieDbContext context,
    IMagpieSystem magpieSystem,
    ISettingsProvider systemSettings) : IDatabaseManagementProvider
{
    private const string NameSeparator = "__";
    private const int MaxNameLength = 80;

    /// <summary>
    /// Strict format for backup ids produced by <see cref="BackupAsync"/>:
    /// <c>yyyyMMdd_HHmmss</c> with an optional <c>_NNN</c> millisecond suffix.
    /// Any other shape (path separators, traversal segments, etc.) is rejected before
    /// the value is interpolated into a filesystem path.
    /// </summary>
    private static readonly Regex BackupIdFormat = new(
        @"^\d{8}_\d{6}(_\d{3})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public string Provider => "SQLite";

    /// <inheritdoc />
    public async Task<string> BackupAsync(string? name, CancellationToken cancellationToken)
    {
        var connectionString = context.Database.GetDbConnection().ConnectionString
            ?? throw new InvalidOperationException();
        var backupDirectory = await GetBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(backupDirectory);

        var now = DateTime.UtcNow;
        var timestamp = now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var displayName = string.IsNullOrWhiteSpace(name)
            ? $"Scheduled {timestamp}"
            : name.Trim();
        var sanitizedName = SanitizeName(displayName);

        var backupId = timestamp;
        if (ResolveBackupPath(backupDirectory, backupId) is not null)
        {
            // Same-second collision; disambiguate with milliseconds.
            backupId = $"{timestamp}_{now.Millisecond:D3}";
            if (ResolveBackupPath(backupDirectory, backupId) is not null)
            {
                throw new ArgumentException($"Backup '{backupId}' already exists.", nameof(name));
            }
        }

        var backupFileName = string.IsNullOrEmpty(sanitizedName)
            ? $"{backupId}.db"
            : $"{backupId}{NameSeparator}{sanitizedName}.db";
        var backupPath = Path.Combine(backupDirectory, backupFileName);

        var backupConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = backupPath
        };

        using var sourceConnection = new SqliteConnection(connectionString);
        using var backupConnection = new SqliteConnection(backupConnectionString.ConnectionString);

        await sourceConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await backupConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

        sourceConnection.BackupDatabase(backupConnection);

        return backupId;
    }

    /// <inheritdoc />
    public async Task<bool> RestoreAsync(string backupId, CancellationToken cancellationToken)
    {
        ValidateBackupId(backupId, nameof(backupId));

        var connectionString = context.Database.GetDbConnection().ConnectionString
            ?? throw new InvalidOperationException();
        var backupDirectory = await GetBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);
        var backupPath = ResolveBackupPath(backupDirectory, backupId);

        if (backupPath is null)
        {
            return false;
        }

        var backupConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = backupPath
        };

        using var destinationConnection = new SqliteConnection(connectionString);
        using var backupConnection = new SqliteConnection(backupConnectionString.ConnectionString);

        await destinationConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await backupConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

        backupConnection.BackupDatabase(destinationConnection);

        return true;
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
            let parsed = ParseBackupFileName(Path.GetFileNameWithoutExtension(path))
            let info = new FileInfo(path)
            orderby info.CreationTimeUtc descending
            select new BackupInfo(parsed.Id, parsed.Name, info.CreationTimeUtc, info.Length)).ToList();

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
        var settingsGroup = await systemSettings.GetSettingsGroupAsync(
            Constants.SettingsGroupName,
            cancellationToken).ConfigureAwait(false);

        var backupFolder = settingsGroup.Settings.TryGetValue(Constants.BackupDirectorySettingKey, out var value)
            ? value
            : Constants.DefaultBackupDirectory;

        if (!Path.IsPathRooted(backupFolder))
        {
            backupFolder = Path.Combine(magpieSystem.SystemRoot, backupFolder);
        }

        return backupFolder;
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

    /// <summary>
    /// Rejects backup ids that do not match the strict timestamp format produced by
    /// <see cref="BackupAsync"/>. This blocks path-traversal payloads (e.g. <c>../../etc/passwd</c>,
    /// <c>foo/../bar</c>, <c>..\bad</c>) before any value reaches <see cref="Path.Combine(string, string)"/>
    /// or <see cref="Directory.GetFiles(string, string)"/>.
    /// </summary>
    private static void ValidateBackupId(string backupId, string parameterName)
    {
        if (string.IsNullOrEmpty(backupId))
        {
            throw new ArgumentException("Backup id is required.", parameterName);
        }

        if (!BackupIdFormat.IsMatch(backupId))
        {
            throw new ArgumentException(
                $"Backup id '{backupId}' is not in the expected format.",
                parameterName);
        }
    }
}
