using System;
using System.Collections.Generic;
using System.IO;

using Magpie.Models.Data;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Magpie.Data.SQLite;

/// <summary>
/// The factory for SQLite-backed <see cref="MagpieDbContext"/> instances.
/// </summary>
public sealed class MagpieDbContextFactory : IDesignTimeDbContextFactory<MagpieDbContext>
{
    private static readonly TimeSpan BusyTimeout = TimeSpan.FromSeconds(10);

    private const int DefaultCommandTimeoutSeconds = 30;

    /// <summary>
    /// Creates a context for the given connection string.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string</param>
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
        var tunedConnectionString = new SqliteConnectionStringBuilder(connectionString)
        {
            DefaultTimeout = DefaultCommandTimeoutSeconds
        }.ConnectionString;

        optionsBuilder
            .UseSqlite(
                tunedConnectionString,
                sqlite => sqlite
                    .MigrationsAssembly(typeof(MagpieDbContextFactory).Assembly.GetName().Name)
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .ReplaceService<IModelCustomizer, SqliteModelCustomizer>()
            .AddInterceptors(new SqlitePragmaConnectionInterceptor(BusyTimeout))
            .UseMagpieSeeding(defaultScheduleTriggers, defaultEventTriggers);
    }

    private static string GetDesignTimeConnectionString()
    {
        var assemblyLocation = typeof(MagpieDbContextFactory).Assembly.Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation)
            ?? throw new InvalidOperationException(
                $"Could not determine the directory of assembly {assemblyLocation}.");

        var projectRoot = FindProjectRoot(assemblyDir);
        var dataDir = Path.Combine(projectRoot, "data");

        Directory.CreateDirectory(dataDir);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDir, "magpie.db")
        };

        return builder.ConnectionString;
    }

    private static string FindProjectRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Magpie.sln")))
            {
                return current.Parent?.FullName ?? current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find project root.");
    }

    /// <inheritdoc />
    public MagpieDbContext CreateDbContext(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var connectionString = args.Length > 0 ? args[0] : GetDesignTimeConnectionString();

        return Create(connectionString, [], []);
    }
}
