using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Sqlite.Migrations.Internal;
using Microsoft.Extensions.Logging;

namespace Magpie.Data.SQLite;

#pragma warning disable EF1001
internal sealed class MigrationLockSqliteHistoryRepository(HistoryRepositoryDependencies dependencies) :
    SqliteHistoryRepository(dependencies)
{
    private sealed class FileLock(IHistoryRepository historyRepository, FileStream file) : IMigrationsDatabaseLock
    {
        public IHistoryRepository HistoryRepository => historyRepository;

        public void Dispose()
        {
            file.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            return file.DisposeAsync();
        }
    }

    [SuppressMessage("Ordering", "SA1201", Justification = "Nested types come first in this codebase.")]
    private static readonly int ConflictHResult = OperatingSystem.IsWindows()
        ? unchecked((int)0x80070020)
        : OperatingSystem.IsLinux() ? 11 : 35;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    public override LockReleaseBehavior LockReleaseBehavior => LockReleaseBehavior.Explicit;

    private static string ResolveLinks(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        if (parent is not null)
        {
            fullPath = Path.Combine(ResolveLinks(parent), Path.GetFileName(fullPath));
        }

        FileSystemInfo entry = Directory.Exists(fullPath) ? new DirectoryInfo(fullPath) : new FileInfo(fullPath);
        return entry.Exists && entry.LinkTarget is not null
            ? entry.ResolveLinkTarget(returnFinalTarget: true)!.FullName
            : fullPath;
    }

    public override IMigrationsDatabaseLock AcquireDatabaseLock()
    {
        return AcquireDatabaseLockAsync().GetAwaiter().GetResult();
    }

    public override async Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = new SqliteConnectionStringBuilder(Dependencies.Connection.DbConnection.ConnectionString);
        if (connectionString.Mode is SqliteOpenMode.Memory || connectionString.DataSource is ":memory:" or "")
        {
            return await base.AcquireDatabaseLockAsync(cancellationToken).ConfigureAwait(false);
        }

        var lockPath = ResolveLinks(connectionString.DataSource) + ".lock";
        var waiting = false;
        while (true)
        {
            try
            {
                var file = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                return new FileLock(this, file);
            }
            catch (IOException ex) when (ex.HResult == ConflictHResult)
            {
                if (!waiting)
                {
                    Dependencies.MigrationsLogger.Logger.LogWarning(
                        "Waiting for another process to migrate the SQLite database {DatabasePath}",
                        connectionString.DataSource);
                    waiting = true;
                }
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
#pragma warning restore EF1001
