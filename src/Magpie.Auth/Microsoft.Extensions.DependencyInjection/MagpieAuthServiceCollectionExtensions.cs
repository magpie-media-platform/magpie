using System;
using System.Linq;
using System.Security.Claims;

using Magpie.Abstractions.Auth;
using Magpie.Auth;
using Magpie.Models.Enums;
using Magpie.Sdk.Auth;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

#pragma warning disable IDE0130
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>
/// The dependency-injection registration extensions for Magpie authentication.
/// </summary>
public static class MagpieAuthServiceCollectionExtensions
{
    private const string ConfigurationSectionName = "Magpie:Auth";

    /// <summary>
    /// Registers the Magpie authentication and authorization services.
    /// </summary>
    /// <param name="services">The service collection to register into</param>
    /// <param name="configuration">The configuration source for the authentication options</param>
    public static IServiceCollection AddMagpieAuth(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(configuration.GetSection(ConfigurationSectionName).Get<TokenServiceOptions>() ?? new());
        services.AddSingleton(static provider => provider.GetRequiredService<TokenServiceOptions>().LockoutOptions);

        services.AddSingleton<CryptoProviderFactory, KeyStoreHmacCryptoProviderFactory>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<ICredentialAuthenticationProvider, LocalCredentialAuthenticationProvider>();
        services.AddScoped<IAuthenticationProvider, JwtBearerAuthenticationProvider>();

        services.AddScoped<IAuthService, AuthService>();

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(nameof(Role.Viewer), policy => policy.RequireAssertion(c => HasAtLeast(c, Role.Viewer)))
            .AddPolicy(nameof(Role.Requester), policy => policy.RequireAssertion(c => HasAtLeast(c, Role.Requester)))
            .AddPolicy(nameof(Role.Manager), policy => policy.RequireAssertion(c => HasAtLeast(c, Role.Manager)))
            .AddPolicy(nameof(Role.Admin), policy => policy.RequireAssertion(c => HasAtLeast(c, Role.Admin)));

        return services;
    }

    /// <summary>
    /// Registers the Magpie authentication scheme for API key and bearer token requests.
    /// </summary>
    /// <param name="services">The service collection to register into</param>
    public static IServiceCollection AddMagpieAuthenticationScheme(this IServiceCollection services)
    {
        services
            .AddAuthentication(MagpieAuthSchemes.Magpie)
            .AddScheme<MagpieSchemeOptions, MagpieAuthenticationHandler>(MagpieAuthSchemes.Magpie, _ => { });

        return services;
    }

    private static bool HasAtLeast(AuthorizationHandlerContext context, Role required)
    {
        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value);

        foreach (var raw in roles)
        {
            if (int.TryParse(raw, out _))
            {
                continue;
            }

            if (Enum.TryParse<Role>(raw, ignoreCase: true, out var parsed) &&
                Enum.IsDefined(parsed) &&
                parsed >= required)
            {
                return true;
            }
        }

        return false;
    }
}
