using System;
using System.IO;

using Magpie.Api;
using Magpie.App.Extensions;
using Magpie.Auth;
using Magpie.Core;
using Magpie.Data.MySQL;
using Magpie.Data.PostgreSQL;
using Magpie.Data.SQLite;
using Magpie.Data.SqlServer;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace Magpie.App;

/// <summary>
/// Composition root for the Magpie host. Reads configuration to pick a database
/// provider, then wires every Magpie layer into the host's service collection.
/// </summary>
public static class MagpieHostExtensions
{
    /// <summary>
    /// Configuration key for the database provider name (sqlite, postgresql, mysql, sqlserver).
    /// </summary>
    public const string DatabaseProviderKey = "Magpie:Database:Provider";

    /// <summary>
    /// Configuration key for the database connection string.
    /// </summary>
    public const string DatabaseConnectionStringKey = "Magpie:Database:ConnectionString";

    /// <summary>
    /// Default database provider when none is configured.
    /// </summary>
    public const string DefaultProvider = "sqlite";

    /// <summary>
    /// Wire every Magpie layer into the host: core services, auth, API endpoint groups,
    /// and the configured database provider. Extensions are loaded separately via
    /// <see cref="MagpieHostBuilder.AddMagpieExtensions"/>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHostApplicationBuilder AddMagpie(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var contentRoot = builder.Environment.ContentRootPath;

        var systemRoot = contentRoot;
        var dataRoot = Path.Combine(contentRoot, "data");
        var extensionsRoot = Path.Combine(contentRoot, "extensions");

        Directory.CreateDirectory(dataRoot);

        var provider = builder.Configuration[DatabaseProviderKey] ?? DefaultProvider;
        var connectionString = builder.Configuration[DatabaseConnectionStringKey];

        builder.Services.AddMagpieCore(systemRoot, extensionsRoot);
        builder.Services.AddMagpieAuth(builder.Configuration);
        builder.Services.AddMagpieApi();

        switch (provider.ToLowerInvariant())
        {
            case "sqlite":
                builder.Services.AddMagpieDataSqlite(connectionString ?? DefaultSqliteConnectionString(dataRoot));
                break;
            case "postgresql" or "postgres":
                builder.Services.AddMagpieDataPostgreSql(connectionString
                    ?? throw new InvalidOperationException(
                        $"Configuration key '{DatabaseConnectionStringKey}' is required for the PostgreSQL provider."));
                break;
            case "mysql":
                builder.Services.AddMagpieDataMySql(connectionString
                    ?? throw new InvalidOperationException(
                        $"Configuration key '{DatabaseConnectionStringKey}' is required for the MySQL provider."));
                break;
            case "sqlserver" or "mssql":
                builder.Services.AddMagpieDataSqlServer(connectionString
                    ?? throw new InvalidOperationException(
                        $"Configuration key '{DatabaseConnectionStringKey}' is required for the SQL Server provider."));
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown database provider '{provider}'. Expected one of: sqlite, postgresql, mysql, sqlserver.");
        }

        return builder;
    }

    private static string DefaultSqliteConnectionString(string dataRoot)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataRoot, "magpie.db")
        }.ConnectionString;
    }
}
