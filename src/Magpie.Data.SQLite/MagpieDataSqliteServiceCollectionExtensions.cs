using System;

using Magpie.Abstractions.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Magpie.Data.SQLite;

/// <summary>
/// DI extensions that register a SQLite-backed <see cref="MagpieDbContext"/> and the
/// SQLite database management provider.
/// </summary>
public static class MagpieDataSqliteServiceCollectionExtensions
{
    /// <summary>
    /// Register the SQLite database provider with the given connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMagpieDataSqlite(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<MagpieDbContext>(
            options => options.UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(typeof(MagpieDbContextFactory).Assembly.GetName().Name)),
            ServiceLifetime.Singleton);

        services.TryAddSingleton<IDatabaseManagementProvider, SQLiteDatabaseManagementProvider>();

        return services;
    }
}
