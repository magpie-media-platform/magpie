using System;
using System.IO;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Magpie.Data.Database.SQLite;

/// <summary>
/// Design-time factory for SQLite migrations.
/// </summary>
public sealed class MagpieDbContextFactory : IDesignTimeDbContextFactory<MagpieDbContext>
{
    /// <inheritdoc />
    public MagpieDbContext CreateDbContext(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var connectionString = args.Length > 0 ? args[0] : GetDesignTimeConnectionString();

        var optionsBuilder = new DbContextOptionsBuilder<MagpieDbContext>();
        optionsBuilder.UseSqlite(
            connectionString,
            options => options.MigrationsAssembly(typeof(MagpieDbContextFactory).Assembly.GetName().Name));

        return new(optionsBuilder.Options);
    }

    private static string GetDesignTimeConnectionString()
    {
        var assemblyLocation = typeof(MagpieDbContextFactory).Assembly.Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation) ?? throw new InvalidOperationException();

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
}
