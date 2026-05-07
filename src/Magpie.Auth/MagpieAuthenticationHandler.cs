using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Security;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Magpie.Auth;

internal sealed class MagpieAuthenticationHandler(
    IOptionsMonitor<MagpieSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IApiKeyManager apiKeyManager,
    IUserManager userManager,
    IEnumerable<IAuthenticationProvider> providers) : AuthenticationHandler<MagpieSchemeOptions>(
        options,
        loggerFactory,
        encoder)
{
    public const string SchemeName = MagpieAuthSchemes.Magpie;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.Request.Headers[Constants.ApiKeyHeader].FirstOrDefault() is { Length: > 0 } apiKey)
        {
            var validatedKey = await apiKeyManager.TryValidateApiKeyAsync(apiKey, Context.RequestAborted)
                .ConfigureAwait(false);

            if (validatedKey is not null)
            {
                var user = await userManager.GetUserAsync(validatedKey.UserId, Context.RequestAborted)
                    .ConfigureAwait(false);

                if (user is { Enabled: true })
                {
                    var principal = await userManager.CreatePrincipalAsync(user, Context.RequestAborted)
                        .ConfigureAwait(false);
                    return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
                }
            }
        }

        foreach (var provider in providers)
        {
            var principal = await provider
                .TryAuthenticateAsync(Context, Context.RequestAborted)
                .ConfigureAwait(false);

            if (principal is null)
            {
                continue;
            }

            if (!await ValidateUserAsync(principal).ConfigureAwait(false))
            {
                continue;
            }

            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }

        return AuthenticateResult.Fail("no authentication providers authenticated the request");
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        foreach (var provider in providers)
        {
            await provider.ChallengeAsync(Context, Context.RequestAborted).ConfigureAwait(false);
        }

        await base.HandleChallengeAsync(properties).ConfigureAwait(false);
    }

    private async Task<bool> ValidateUserAsync(ClaimsPrincipal principal)
    {
        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim is not { Value: var id } || !Guid.TryParse(id, out var userId))
        {
            return false;
        }

        var user = await userManager.GetUserAsync(userId, Context.RequestAborted).ConfigureAwait(false);

        return user is { Enabled: true };
    }
}
