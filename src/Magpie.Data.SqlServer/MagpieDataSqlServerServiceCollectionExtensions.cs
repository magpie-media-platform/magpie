using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Data.SqlServer;

/// <summary>
/// DI extensions that register a SQL Server-backed <see cref="MagpieDbContext"/>.
/// </summary>
public static class MagpieDataSqlServerServiceCollectionExtensions
{
    /// <summary>
    /// Register the SQL Server database provider with the given connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMagpieDataSqlServer(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<MagpieDbContext>(
            options => options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(MagpieDbContextFactory).Assembly.GetName().Name)),
            ServiceLifetime.Singleton);

        // No IDatabaseManagementProvider implementation exists for SQL Server yet.

        return services;
    }
}
