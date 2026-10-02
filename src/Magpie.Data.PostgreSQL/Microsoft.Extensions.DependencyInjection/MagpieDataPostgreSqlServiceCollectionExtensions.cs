using System;
using System.Collections.Generic;

using Magpie.Abstractions.Database;
using Magpie.Data;
using Magpie.Data.PostgreSQL;
using Magpie.Models.Data;
using Magpie.Sdk.Extensions;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The dependency-injection registration extensions for the PostgreSQL data layer.
/// </summary>
public static class MagpieDataPostgreSqlServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PostgreSQL data layer for the given connection string.
    /// </summary>
    /// <param name="services">The service collection to register into</param>
    /// <param name="connectionString">The PostgreSQL connection string</param>
    /// <param name="defaultScheduleTriggers">The default schedule triggers to seed</param>
    /// <param name="defaultEventTriggers">The default event triggers to seed</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMagpieDataPostgreSql(
        this IServiceCollection services,
        string connectionString,
        IReadOnlyList<ScheduleTrigger> defaultScheduleTriggers,
        IReadOnlyList<EventTrigger> defaultEventTriggers)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<MagpieDbContext>(
            options => MagpieDbContextFactory.ConfigureOptions(
                options,
                connectionString,
                defaultScheduleTriggers,
                defaultEventTriggers));

        services.TryAddScoped<IDatabaseManagementProvider, PostgreSqlDatabaseManagementProvider>();

        return services;
    }

    /// <summary>
    /// Registers the PostgreSQL cluster backplane.
    /// </summary>
    /// <param name="services">The service collection to register into</param>
    /// <param name="connectionString">The PostgreSQL connection string</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMagpieDataPostgreSqlClusterBackplane(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton<IClusterBackplane>(sp => new PostgreSqlClusterBackplane(
            connectionString,
            sp.GetRequiredService<ILogger<PostgreSqlClusterBackplane>>()));

        return services;
    }
}
