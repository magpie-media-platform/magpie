using System;
using System.Linq;
using System.Security.Claims;

using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Security;
using Magpie.Models.Enums;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Magpie.Auth;

/// <summary>
/// DI extensions that wire the Magpie authentication scheme, authorization policies,
/// and token service into the host.
/// </summary>
public static class MagpieAuthServiceCollectionExtensions
{
    /// <summary>
    /// Register the Magpie authentication scheme, role-based authorization policies, and
    /// the token service. Reads <c>Magpie:Auth</c> configuration to populate
    /// <see cref="TokenServiceOptions"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMagpieAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var tokenOptions = new TokenServiceOptions();
        configuration.GetSection("Magpie:Auth").Bind(tokenOptions);
        services.TryAddSingleton(tokenOptions);

        services.TryAddSingleton<ITokenService, TokenService>();

        // Register LocalCredentialAuthenticationProvider once and expose it through every
        // interface it implements, so the IEnumerable<IAuthenticationProvider> consumed by
        // MagpieAuthenticationHandler resolves a single instance.
        services.TryAddSingleton<UserManager>();
        services.TryAddSingleton<IUserManager>(sp => sp.GetRequiredService<UserManager>());
        services.TryAddSingleton<LocalCredentialAuthenticationProvider>();
        services.TryAddSingleton<ICredentialAuthenticationProvider>(
            sp => sp.GetRequiredService<LocalCredentialAuthenticationProvider>());
        services.TryAddSingleton<ICredentialManagementAuthenticationProvider>(
            sp => sp.GetRequiredService<LocalCredentialAuthenticationProvider>());
        services.AddSingleton<IAuthenticationProvider>(
            sp => sp.GetRequiredService<LocalCredentialAuthenticationProvider>());

        // Local provider returns null for request-time auth; bearer handles bearer tokens.
        // Both registered as IAuthenticationProvider for the handler to iterate.
        services.TryAddSingleton<JwtBearerAuthenticationProvider>();
        services.AddSingleton<IAuthenticationProvider>(
            sp => sp.GetRequiredService<JwtBearerAuthenticationProvider>());

        services
            .AddAuthentication(MagpieAuthSchemes.Magpie)
            .AddScheme<MagpieSchemeOptions, MagpieAuthenticationHandler>(
                MagpieAuthSchemes.Magpie,
                _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy(nameof(Role.Viewer), policy => policy.RequireAssertion(c => HasRoleAtLeast(c, Role.Viewer)))
            .AddPolicy(nameof(Role.Requester), policy => policy.RequireAssertion(c => HasRoleAtLeast(c, Role.Requester)))
            .AddPolicy(nameof(Role.Manager), policy => policy.RequireAssertion(c => HasRoleAtLeast(c, Role.Manager)))
            .AddPolicy(nameof(Role.Admin), policy => policy.RequireAssertion(c => HasRoleAtLeast(c, Role.Admin)));

        return services;
    }

    private static bool HasRoleAtLeast(AuthorizationHandlerContext context, Role required)
    {
        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value);
        foreach (var raw in roles)
        {
            if (Enum.TryParse<Role>(raw, ignoreCase: true, out var parsed) && parsed >= required)
            {
                return true;
            }
        }

        return false;
    }
}
