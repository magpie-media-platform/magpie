using System;
using System.Collections.Generic;

using Magpie.Abstractions.Database;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models.Data;

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The dependency-injection registration extensions for the SQLite data layer.
/// </summary>
public static class MagpieDataSqliteServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite data layer for the given connection string.
    /// </summary>
    /// <param name="services">The service collection to register into</param>
    /// <param name="connectionString">The SQLite connection string</param>
    /// <param name="defaultScheduleTriggers">The default schedule triggers to seed</param>
    /// <param name="defaultEventTriggers">The default event triggers to seed</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMagpieDataSqlite(
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

        services.TryAddScoped<IDatabaseManagementProvider, SQLiteDatabaseManagementProvider>();

        return services;
    }
}
