using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Magpie.Data.SQLite;

internal sealed class SqlitePragmaConnectionInterceptor : DbConnectionInterceptor
{
    private const string PragmaSql = "PRAGMA busy_timeout = 10000; PRAGMA journal_mode = WAL;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SqliteUnicodeLower.Register((SqliteConnection)connection);
        using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        SqliteUnicodeLower.Register((SqliteConnection)connection);
        using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
