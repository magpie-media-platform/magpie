using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Internal;

namespace Magpie.Data.PostgreSQL;

#pragma warning disable EF1001
internal sealed class MigrationLockPostgreSqlHistoryRepository(HistoryRepositoryDependencies dependencies) :
    NpgsqlHistoryRepository(dependencies)
{
    private sealed class SessionLock(IHistoryRepository historyRepository, NpgsqlConnection connection) :
        IMigrationsDatabaseLock
    {
        public IHistoryRepository HistoryRepository => historyRepository;

        public void Dispose()
        {
            connection.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            return connection.DisposeAsync();
        }
    }

    [SuppressMessage("Ordering", "SA1201", Justification = "Nested types come first in this codebase.")]
    private static readonly long MigrationLockKey = Convert.ToInt64(Convert.ToHexString("magpie"u8), 16);

    public override LockReleaseBehavior LockReleaseBehavior => LockReleaseBehavior.Explicit;

    protected override string ExistsSql =>
        "SELECT to_regclass(" +
        Dependencies.TypeMappingSource.GetMapping(typeof(string))
            .GenerateSqlLiteral(SqlGenerationHelper.DelimitIdentifier(TableName, TableSchema)) +
        ") IS NOT NULL;";

    public override bool Exists()
    {
        return InterpretExistsResult(
            Dependencies.RawSqlCommandBuilder.Build(ExistsSql).ExecuteScalar(CreateParameters()));
    }

    public override async Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        return InterpretExistsResult(
            await Dependencies.RawSqlCommandBuilder.Build(ExistsSql)
                .ExecuteScalarAsync(CreateParameters(), cancellationToken)
                .ConfigureAwait(false));
    }

    public override IMigrationsDatabaseLock AcquireDatabaseLock()
    {
        return AcquireDatabaseLockAsync().GetAwaiter().GetResult();
    }

    public override async Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(
        CancellationToken cancellationToken = default)
    {
        Dependencies.MigrationsLogger.AcquiringMigrationLock();

        var builder = new NpgsqlConnectionStringBuilder(Dependencies.Connection.ConnectionString) { Pooling = false };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key);", connection) { CommandTimeout = 0 };
            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("key", MigrationLockKey);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new SessionLock(this, connection);
    }

    private RelationalCommandParameterObject CreateParameters()
    {
        return new(
            Dependencies.Connection,
            null,
            null,
            Dependencies.CurrentContext.Context,
            Dependencies.CommandLogger,
            CommandSource.Migrations);
    }
}
#pragma warning restore EF1001
