using System;
using System.Collections.Generic;

using Magpie.Abstractions.Database;
using Magpie.Data;
using Magpie.Data.SqlServer;
using Magpie.Models.Data;
using Magpie.Sdk.Extensions;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>
/// The dependency-injection registration extensions for the SQL Server data layer.
/// </summary>
public static class MagpieDataSqlServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQL Server data layer for the given connection string.
    /// </summary>
    /// <param name="services">The service collection to register into</param>
    /// <param name="connectionString">The SQL Server connection string</param>
    /// <param name="defaultScheduleTriggers">The default schedule triggers to seed</param>
    /// <param name="defaultEventTriggers">The default event triggers to seed</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMagpieDataSqlServer(
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

        services.TryAddScoped<IDatabaseManagementProvider, SqlServerDatabaseManagementProvider>();

        return services;
    }

    /// <summary>
    /// If no backplane exists, registers the SQL Server relay-table cluster backplane.
    /// </summary>
    /// <param name="services">The service collection to register into</param>
    /// <param name="connectionString">The SQL Server connection string</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMagpieDataSqlServerClusterBackplane(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton<IClusterBackplane>(sp => new SqlServerClusterBackplane(
            connectionString,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<SqlServerClusterBackplane>>()));

        return services;
    }
}
