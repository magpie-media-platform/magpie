using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Data.PostgreSQL;

/// <summary>
/// DI extensions that register a PostgreSQL-backed <see cref="MagpieDbContext"/>.
/// </summary>
public static class MagpieDataPostgreSqlServiceCollectionExtensions
{
    /// <summary>
    /// Register the PostgreSQL database provider with the given connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMagpieDataPostgreSql(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<MagpieDbContext>(
            options => options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(MagpieDbContextFactory).Assembly.GetName().Name)),
            ServiceLifetime.Singleton);

        // No IDatabaseManagementProvider implementation exists for PostgreSQL yet.

        return services;
    }
}
