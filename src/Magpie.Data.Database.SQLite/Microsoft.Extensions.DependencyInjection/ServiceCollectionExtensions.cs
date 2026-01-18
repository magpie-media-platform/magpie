using Magpie.Abstractions.Database;
using Magpie.Data.Database;
using Magpie.Data.Database.SQLite;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130 
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130 

/// <summary>
/// Service collection extensions for SQLite database provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    private static volatile bool _walModeInitialized;

    /// <summary>
    /// Add SQLite database management provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQLite connection string.</param>
    public static IServiceCollection AddSQLiteDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        _ = services.AddDbContext<MagpieDbContext>(options =>
        {
            if (!_walModeInitialized)
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA journal_mode=WAL;";
                    command.ExecuteNonQuery();
                }

                _walModeInitialized = true;
            }
            options.UseSqlite(connectionString);
        });
        services.AddScoped<IDatabaseManagementProvider, SQLiteDatabaseManagementProvider>();

        return services;
    }
}
