using System;

using Microsoft.Extensions.Http.Resilience;

#pragma warning disable IDE0130
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>
/// The dependency-injection registration extensions for outbound HTTP resilience.
/// </summary>
public static class MagpieHttpResilienceServiceCollectionExtensions
{
    /// <summary>
    /// Registers standard HTTP resilience for every <see cref="System.Net.Http.HttpClient"/>.
    /// </summary>
    /// <param name="services">The service collection to register into</param>
    /// <returns>The same service collection</returns>
    public static IServiceCollection AddMagpieHttpResilience(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.ConfigureHttpClientDefaults(builder =>
            builder.AddStandardResilienceHandler().SelectPipelineByAuthority());

        return services;
    }
}
