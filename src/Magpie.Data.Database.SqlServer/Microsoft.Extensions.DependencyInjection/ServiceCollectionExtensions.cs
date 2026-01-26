using Magpie.Data.Database;

using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130 
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130 

/// <summary>
/// Service collection extensions for SQL Server database provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add SQL Server database provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    public static IServiceCollection AddSqlServerDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        _ = services.AddDbContext<MagpieDbContext>(options => options.UseSqlServer(connectionString));

        return services;
    }
}
