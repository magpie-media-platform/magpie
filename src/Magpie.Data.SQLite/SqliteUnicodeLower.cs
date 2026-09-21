using Microsoft.Data.Sqlite;

namespace Magpie.Data.SQLite;

internal static class SqliteUnicodeLower
{
    internal static void Register(SqliteConnection connection)
    {
        connection.CreateFunction("lower", (string? value) => value?.ToLowerInvariant(), isDeterministic: true);
    }
}
