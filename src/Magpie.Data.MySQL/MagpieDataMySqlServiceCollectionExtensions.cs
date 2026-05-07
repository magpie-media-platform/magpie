using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Data.MySQL;

/// <summary>
/// DI extensions that register a MySQL-backed <see cref="MagpieDbContext"/>.
/// </summary>
public static class MagpieDataMySqlServiceCollectionExtensions
{
    /// <summary>
    /// Register the MySQL database provider with the given connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The MySQL connection string.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMagpieDataMySql(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<MagpieDbContext>(
            options => options.UseMySQL(
                connectionString,
                mysql => mysql.MigrationsAssembly(typeof(MagpieDbContextFactory).Assembly.GetName().Name)),
            ServiceLifetime.Singleton);

        // No IDatabaseManagementProvider implementation exists for MySQL yet.

        return services;
    }
}
