using System;
using System.Collections.Generic;

using Magpie.Models;
using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Magpie.Data.PostgreSQL;

/// <summary>
/// The factory for PostgreSQL-backed <see cref="MagpieDbContext"/> instances.
/// </summary>
public sealed class MagpieDbContextFactory : IDesignTimeDbContextFactory<MagpieDbContext>
{
    private const int CommandTimeoutSeconds = 300;

    /// <summary>
    /// Creates a context for the given connection string.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string</param>
    /// <param name="defaultScheduleTriggers">The default schedule triggers to seed</param>
    /// <param name="defaultEventTriggers">The default event triggers to seed</param>
    public static MagpieDbContext Create(
        string connectionString,
        IReadOnlyList<ScheduleTrigger> defaultScheduleTriggers,
        IReadOnlyList<EventTrigger> defaultEventTriggers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var optionsBuilder = new DbContextOptionsBuilder<MagpieDbContext>();
        ConfigureOptions(optionsBuilder, connectionString, defaultScheduleTriggers, defaultEventTriggers);

        return new(optionsBuilder.Options);
    }

    internal static void ConfigureOptions(
        DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        IReadOnlyList<ScheduleTrigger> defaultScheduleTriggers,
        IReadOnlyList<EventTrigger> defaultEventTriggers,
        DatabaseMigrationState? migrationState = null)
    {
        optionsBuilder
            .UseNpgsql(
                connectionString,
                npgsql => npgsql
                    .CommandTimeout(CommandTimeoutSeconds)
                    .MigrationsAssembly(typeof(MagpieDbContextFactory).Assembly.GetName().Name)
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .ReplaceService<IModelCustomizer, PostgreSqlModelCustomizer>()
            .ReplaceService<IHistoryRepository, MigrationLockPostgreSqlHistoryRepository>()
            .UseMagpieSeeding(defaultScheduleTriggers, defaultEventTriggers, migrationState);
    }

    private static string GetDesignTimeConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Port = 5432,
            Database = "magpie",
            Username = "postgres",
            Password = "postgres"
        };

        return builder.ConnectionString;
    }

    /// <inheritdoc />
    public MagpieDbContext CreateDbContext(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var connectionString = args.Length > 0 ? args[0] : GetDesignTimeConnectionString();

        return Create(connectionString, [], []);
    }
}
