using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

using Magpie.Sdk.Auth;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Magpie.Auth;

internal sealed class MagpieAuthenticationHandler(
    IOptionsMonitor<MagpieSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IApiKeyService apiKeyService,
    IUserService userService,
    IEnumerable<IAuthenticationProvider> providers) : AuthenticationHandler<MagpieSchemeOptions>(
        options,
        loggerFactory,
        encoder)
{
    private const string ApiKeyHeader = "X-Api-Key";

    public const string SchemeName = MagpieAuthSchemes.Magpie;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        (string Provider, string Reason)? rejection = null;

        if (Context.Request.Headers[ApiKeyHeader].FirstOrDefault() is { Length: > 0 } apiKey)
        {
            var validatedKey = await apiKeyService.TryValidateAsync(apiKey, Context.RequestAborted)
                .ConfigureAwait(false);

            if (validatedKey is not null)
            {
                var user = await userService.GetAuthenticationUserAsync(validatedKey.UserId, Context.RequestAborted)
                    .ConfigureAwait(false);

                if (user is { Enabled: true })
                {
                    var principal = user.ToPrincipal();
                    return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
                }
            }

            rejection = ("api key", "the key is not valid or its user cannot sign in");
        }

        foreach (var provider in providers)
        {
            var attempt = await provider
                .TryAuthenticateAsync(Context, Context.RequestAborted)
                .ConfigureAwait(false);

            if (attempt is null)
            {
                continue;
            }

            if (attempt.Status is AuthenticationAttemptStatus.Rejected)
            {
                rejection ??= (provider.ProviderType, attempt.RejectionReason ?? "no reason was given");
                continue;
            }

            if (attempt is not { Status: AuthenticationAttemptStatus.Authenticated, Principal: { } principal })
            {
                continue;
            }

            if (!await ValidateUserAsync(principal).ConfigureAwait(false))
            {
                rejection ??= (provider.ProviderType, "the authenticated user cannot sign in");
                continue;
            }

            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }

        if (rejection is not { } failure)
        {
            return AuthenticateResult.NoResult();
        }

        Logger.LogInformation(
            "Authentication failed for the {Provider} credential because {Reason}",
            failure.Provider,
            failure.Reason);

        return AuthenticateResult.Fail($"the {failure.Provider} credential was rejected: {failure.Reason}");
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

        var user = await userService.GetAuthenticationUserAsync(userId, Context.RequestAborted)
            .ConfigureAwait(false);

        return user is { Enabled: true };
    }
}
