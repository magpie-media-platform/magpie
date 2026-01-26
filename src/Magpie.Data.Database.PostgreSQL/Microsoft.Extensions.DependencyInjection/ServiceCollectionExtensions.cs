using Magpie.Data.Database;

using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130 
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130 

/// <summary>
/// Service collection extensions for PostgreSQL database provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add PostgreSQL database provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    public static IServiceCollection AddPostgreSQLDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        _ = services.AddDbContext<MagpieDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }
}
