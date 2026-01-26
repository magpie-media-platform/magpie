using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Data;
using Magpie.Abstractions.Security;
using Magpie.Auth.Providers;
using Magpie.Core;
using Magpie.Models.Result;
using Magpie.Models.Users;

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
    IUserRepository userRepository,
    IAuthProviderRepository authProviderRepository,
    IEnumerable<IAuthenticationProvider> providers,
    IEnumerable<IAuthenticator> authenticators) : AuthenticationHandler<MagpieSchemeOptions>(
        options,
        loggerFactory,
        encoder)
{
    public const string SchemeName = "Magpie";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.Request.Headers[Constants.ApiKeyHeader].FirstOrDefault() is { Length: > 0 } apiKey)
        {
            var apiKeyResult = await apiKeyManager.ValidateApiKeyAsync(apiKey, Context.RequestAborted)
                .BindAsync(key => userManager.GetUserAsync(key.UserId, Context.RequestAborted)
                .BindAsync(async user => user switch
                {
                    User u when !u.Enabled =>
                        Result.Fail<ClaimsPrincipal>("invalid user", ErrorCodes.ApiKeyUserDisabled),
                    User u =>
                        await userManager.CreatePrincipalAsync(u, Context.RequestAborted).ConfigureAwait(false),
                    null =>
                        Result.Fail<ClaimsPrincipal>("invalid user", ErrorCodes.ApiKeyUserNotFound)
                }))
                .ConfigureAwait(false);

            if (apiKeyResult is SuccessResult<ClaimsPrincipal> { Value: var principal })
            {
                return AuthenticateResult.Success(new(principal, Scheme.Name));
            }
        }

        foreach (var provider in providers)
        {
            var attempt = await provider
                .TryAuthenticateAsync(Context, Context.RequestAborted)
                .ConfigureAwait(false);

            if (attempt is not SuccessResult<ClaimsPrincipal?> { Value: { } principal })
            {
                continue;
            }
            if (!await ValidateProviderAsync(principal, provider.ProviderType).ConfigureAwait(false))
            {
                continue;
            }
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }

        var jitResult = await TryJitProvisionAsync().ConfigureAwait(false);
        if (jitResult is SuccessResult<ClaimsPrincipal> { Value: var jitPrincipal })
        {
            return AuthenticateResult.Success(new AuthenticationTicket(jitPrincipal, Scheme.Name));
        }

        return AuthenticateResult.Fail("No valid credentials provided");
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        foreach (var provider in providers)
        {
            provider.Challenge(Context);
        }
        return base.HandleChallengeAsync(properties);
    }

    private async Task<bool> ValidateProviderAsync(ClaimsPrincipal principal, string providerType)
    {
        if (providerType == NoAuthProvider.ProviderTypeName)
        {
            return true;
        }

        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim is not { Value: var id } || !Guid.TryParse(id, out var userId))
        {
            return false;
        }

        var userResult = await userRepository.GetByIdAsync(userId, Context.RequestAborted).ConfigureAwait(false);
        if (userResult is not SuccessResult<User?> { Value: { } user })
        {
            return false;
        }
        if (!user.Enabled)
        {
            return false;
        }
        if (user.AuthProviderId is null)
        {
            return false;
        }

        var authProviderResult = await authProviderRepository.GetByIdAsync(
            user.AuthProviderId.Value,
            Context.RequestAborted).ConfigureAwait(false);
        if (authProviderResult is not SuccessResult<AuthProvider?> { Value: { } authProvider })
        {
            return false;
        }
        if (!authProvider.Enabled)
        {
            return false;
        }

        return true;
    }

    private async Task<Result<ClaimsPrincipal>> TryJitProvisionAsync()
    {
        var credentials = ExtractBasicCredentials();
        if (credentials is not var (username, password))
        {
            return Result.Fail<ClaimsPrincipal>("No credentials for JIT provisioning", ErrorCodes.InvalidCredentials);
        }

        foreach (var authenticator in authenticators.Where(a => a.SupportsJitProvisioning))
        {
            var providersResult = await authProviderRepository
                .GetByProviderTypeAsync(authenticator.ProviderType, Context.RequestAborted)
                .ConfigureAwait(false);

            if (providersResult is not SuccessResult<IReadOnlyList<AuthProvider>> { Value: var authProviders })
            {
                continue;
            }

            foreach (var provider in authProviders.Where(p => p.Enabled))
            {
                var authResult = await authenticator
                    .AuthenticateAsync(username, password, provider, Context.RequestAborted)
                    .ConfigureAwait(false);

                if (authResult is SuccessResult<User> { Value: var transientUser })
                {
                    var providerData = transientUser.ProviderData?
                        .Where(kvp => kvp.Value is not null)
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!)
                        ?? [];

                    var createResult = await userManager.CreateExternalUserAsync(
                        transientUser.Username,
                        transientUser.Email,
                        provider.Id,
                        transientUser.ExternalId,
                        transientUser.Role,
                        enabled: true,
                        providerData,
                        createdByUserId: null,
                        Context.RequestAborted).ConfigureAwait(false);

                    if (createResult is SuccessResult<User> { Value: var user })
                    {
                        return await userManager.CreatePrincipalAsync(user, Context.RequestAborted)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        return Result.Fail<ClaimsPrincipal>("Invalid credentials", ErrorCodes.InvalidCredentials);
    }

    private (string Username, string Password)? ExtractBasicCredentials()
    {
        var authHeader = Context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var encoded = authHeader["Basic ".Length..].Trim();
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            if (decoded.Split(':', 2) is [var username, var password] && !string.IsNullOrWhiteSpace(username))
            {
                return (username, password);
            }
        }
        catch (FormatException)
        {
        }

        return null;
    }
}
