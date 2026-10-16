using System;
using System.Collections.Generic;
using System.IO;

using Magpie.Models;
using Magpie.Models.Data;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Magpie.Data.SQLite;

/// <summary>
/// The factory for SQLite-backed <see cref="MagpieDbContext"/> instances.
/// </summary>
public sealed class MagpieDbContextFactory : IDesignTimeDbContextFactory<MagpieDbContext>
{
    private const int CommandTimeoutSeconds = 30;

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
        IReadOnlyList<EventTrigger> defaultEventTriggers,
        DatabaseMigrationState? migrationState = null)
    {
        optionsBuilder
            .UseSqlite(
                connectionString,
                sqlite => sqlite
                    .CommandTimeout(CommandTimeoutSeconds)
                    .MigrationsAssembly(typeof(MagpieDbContextFactory).Assembly.GetName().Name)
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .ReplaceService<IModelCustomizer, SqliteModelCustomizer>()
            .ReplaceService<IHistoryRepository, MigrationLockSqliteHistoryRepository>()
            .AddInterceptors(new SqlitePragmaConnectionInterceptor())
            .UseMagpieSeeding(defaultScheduleTriggers, defaultEventTriggers, migrationState);
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
