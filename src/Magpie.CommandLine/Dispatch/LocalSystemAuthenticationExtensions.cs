using System;
using System.Security.Claims;

using Magpie.Abstractions.Security;
using Magpie.Models.Enums;

using Microsoft.AspNetCore.Builder;

namespace Magpie.CommandLine.Dispatch;

/// <summary>
/// The authentication shim for the in-process command proxy.
/// </summary>
public static class LocalSystemAuthenticationExtensions
{
    private const string AuthenticationType = "MagpieLocalSystem";

    /// <summary>
    /// Adds the middleware that authenticates every request as the local system account.
    /// </summary>
    /// <param name="app">The application builder to configure</param>
    public static IApplicationBuilder UseLocalSystemAuthentication(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            context.User = CreatePrincipal();
            await next(context).ConfigureAwait(false);
        });
    }

    private static ClaimsPrincipal CreatePrincipal()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"),
            new Claim(ClaimTypes.Name, "system"),
            new Claim(ClaimTypes.Role, Role.Admin.ToString()),
            new Claim(MagpieClaims.AuthProviderType, "system")
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType));
    }
}
