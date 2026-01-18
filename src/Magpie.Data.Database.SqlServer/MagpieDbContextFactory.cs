using System;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Magpie.Data.Database.SqlServer;

/// <summary>
/// Design-time factory for SQL Server migrations.
/// </summary>
/// <remarks>
/// The connection string can be passed as an argument after --:
/// <code>dotnet ef migrations add MyMigration -- "Server=localhost;Database=Magpie;..."</code>
/// If no argument is provided, the default connection string uses LocalDB.
/// </remarks>
public sealed class MagpieDbContextFactory : IDesignTimeDbContextFactory<MagpieDbContext>
{
    /// <inheritdoc />
    public MagpieDbContext CreateDbContext(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var connectionString = args.Length > 0 ? args[0] : GetDesignTimeConnectionString();

        var optionsBuilder = new DbContextOptionsBuilder<MagpieDbContext>();
        optionsBuilder.UseSqlServer(
            connectionString,
            options => options.MigrationsAssembly(typeof(MagpieDbContextFactory).Assembly.GetName().Name));

        return new MagpieDbContext(optionsBuilder.Options);
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
}
