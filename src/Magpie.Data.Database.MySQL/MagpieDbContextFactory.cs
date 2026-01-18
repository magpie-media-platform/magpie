using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using MySql.Data.MySqlClient;

namespace Magpie.Data.Database.MySQL;

/// <summary>
/// Design-time factory for MySQL migrations.
/// </summary>
public sealed class MagpieDbContextFactory : IDesignTimeDbContextFactory<MagpieDbContext>
{
    /// <inheritdoc />
    public MagpieDbContext CreateDbContext(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var connectionString = args.Length > 0 ? args[0] : GetDesignTimeConnectionString();

        var optionsBuilder = new DbContextOptionsBuilder<MagpieDbContext>();
        optionsBuilder.UseMySQL(
            connectionString,
            options => options.MigrationsAssembly(typeof(MagpieDbContextFactory).Assembly.GetName().Name));

        return new MagpieDbContext(optionsBuilder.Options);
    }

    private static string GetDesignTimeConnectionString()
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = "localhost",
            Port = 3306,
            Database = "magpie",
            UserID = "root",
            Password = "root"
        };

        return builder.ConnectionString;
    }
}
