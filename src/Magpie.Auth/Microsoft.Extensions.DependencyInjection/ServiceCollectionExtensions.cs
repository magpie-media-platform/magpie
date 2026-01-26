using System;

using Magpie.Abstractions.Auth;
using Magpie.Auth;
using Magpie.Auth.Providers;

#pragma warning disable IDE0130
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>
/// Service collection extensions for Magpie authentication services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add Magpie authentication services including token service and authentication handler.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Configuration action for token service options.
    /// </param>
    public static IServiceCollection AddMagpieAuth(
        this IServiceCollection services,
        Action<TokenServiceOptions>? configure = null)
    {
        var options = new TokenServiceOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddScoped<ITokenService, TokenService>();

        services.AddAuthentication(MagpieAuthenticationHandler.SchemeName)
            .AddScheme<MagpieSchemeOptions, MagpieAuthenticationHandler>(
                MagpieAuthenticationHandler.SchemeName,
                null);

        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Add the NoAuth authentication provider for development/testing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddNoAuthProvider(this IServiceCollection services)
    {
        services.AddScoped<IAuthenticationProvider, NoAuthProvider>();
        return services;
    }

    /// <summary>
    /// Add the Bearer authentication provider (JWT token validation).
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddBearerAuthProvider(this IServiceCollection services)
    {
        services.AddScoped<IAuthenticationProvider, BearerAuthProvider>();
        return services;
    }

    /// <summary>
    /// Add the Local authenticator for validating credentials against local password hashes.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddLocalAuthenticator(this IServiceCollection services)
    {
        services.AddScoped<IAuthenticator, LocalAuthenticator>();
        return services;
    }
}
