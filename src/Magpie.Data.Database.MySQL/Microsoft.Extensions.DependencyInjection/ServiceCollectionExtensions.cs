using Magpie.Data.Database;

using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130 
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130 

/// <summary>
/// Service collection extensions for MySQL database provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add MySQL database provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The MySQL connection string.</param>
    public static IServiceCollection AddMySQLDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        _ = services.AddDbContext<MagpieDbContext>(options => options.UseMySQL(connectionString));

        return services;
    }
}
