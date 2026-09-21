using System;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Magpie.Data.SQLite;

internal sealed class SqlitePragmaConnectionInterceptor(TimeSpan busyTimeout) : DbConnectionInterceptor
{
    private readonly int _busyTimeoutMilliseconds = (int)Math.Ceiling(busyTimeout.TotalMilliseconds);

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        RegisterUnicodeLower(connection);
        ApplyPragmas(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RegisterUnicodeLower(connection);
        await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private static void RegisterUnicodeLower(DbConnection connection)
    {
        if (connection is SqliteConnection sqlite)
        {
            SqliteUnicodeLower.Register(sqlite);
        }
    }

    private void ApplyPragmas(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = BuildPragmaSql();
        command.ExecuteNonQuery();
    }

    private async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = BuildPragmaSql();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private string BuildPragmaSql()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"PRAGMA busy_timeout = {_busyTimeoutMilliseconds}; PRAGMA journal_mode = WAL;");
    }
}
