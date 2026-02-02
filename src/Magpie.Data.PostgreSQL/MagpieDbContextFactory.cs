using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Npgsql;

namespace Magpie.Data.PostgreSQL;

/// <summary>
/// Design-time factory for PostgreSQL migrations.
/// </summary>
public sealed class MagpieDbContextFactory : IDesignTimeDbContextFactory<MagpieDbContext>
{
    /// <inheritdoc />
    public MagpieDbContext CreateDbContext(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var connectionString = args.Length > 0 ? args[0] : GetDesignTimeConnectionString();

        var optionsBuilder = new DbContextOptionsBuilder<MagpieDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            options => options.MigrationsAssembly(typeof(MagpieDbContextFactory).Assembly.GetName().Name));

        return new(optionsBuilder.Options);
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
}
