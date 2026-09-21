using System;
using System.Collections.Generic;

using Magpie.Models.Data;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Magpie.Data.SqlServer;

/// <summary>
/// The factory for SQL Server-backed <see cref="MagpieDbContext"/> instances.
/// </summary>
public sealed class MagpieDbContextFactory : IDesignTimeDbContextFactory<MagpieDbContext>
{
    private const int CommandTimeoutSeconds = 300;

    /// <summary>
    /// Creates a context for the given connection string.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string</param>
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
        IReadOnlyList<EventTrigger> defaultEventTriggers)
    {
        optionsBuilder
            .UseSqlServer(
                connectionString,
                sql => sql
                    .CommandTimeout(CommandTimeoutSeconds)
                    .MigrationsAssembly(typeof(MagpieDbContextFactory).Assembly.GetName().Name)
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .ReplaceService<IModelCustomizer, SqlServerModelCustomizer>()
            .UseMagpieSeeding(defaultScheduleTriggers, defaultEventTriggers);
    }

    private static string GetDesignTimeConnectionString()
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = "(localdb)\\MSSQLLocalDB",
            InitialCatalog = "Magpie",
            IntegratedSecurity = true,
            TrustServerCertificate = true
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
