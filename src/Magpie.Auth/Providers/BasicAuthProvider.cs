using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Data;
using Magpie.Core;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

using static Magpie.Models.Result.ResultExtensions;

namespace Magpie.Auth.Providers;

internal sealed class BasicAuthProvider(
    IUserRepository userRepository,
    IAuthProviderRepository authProviderRepository,
    IEnumerable<IAuthenticator> authenticators) : IAuthenticationProvider
{
    public const string ProviderTypeName = "basic";

    public string ProviderType => ProviderTypeName;

    public async Task<Result<ClaimsPrincipal?>> TryAuthenticateAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var authHeader = context.Request.Headers[HeaderNames.Authorization].ToString();

        if (string.IsNullOrEmpty(authHeader))
        {
            return Result.Success<ClaimsPrincipal?>(null);
        }

        if (!authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success<ClaimsPrincipal?>(null);
        }

        string decodedCredentials;
        try
        {
            var encodedCredentials = authHeader["Basic ".Length..].Trim();
            decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
        }
        catch (FormatException)
        {
            return Result.Fail<ClaimsPrincipal?>("Invalid base64 encoding", ErrorCodes.InvalidCredentials);
        }

        if (decodedCredentials.Split(':', 2) is not [var username, var password])
        {
            return Result.Fail<ClaimsPrincipal?>("Invalid credentials format", ErrorCodes.InvalidCredentials);
        }
        if (string.IsNullOrWhiteSpace(username))
        {
            return Result.Fail<ClaimsPrincipal?>("Username cannot be empty", ErrorCodes.InvalidCredentials);
        }

        return await userRepository.GetByUsernameAsync(username, cancellationToken)
            .BindAsync(user => user is null
                ? Result.Fail<User>("Invalid username or password", ErrorCodes.InvalidCredentials)
                : !user.AuthProviderId.HasValue
                    ? Result.Fail<User>("User has no auth provider configured", ErrorCodes.InvalidCredentials)
                    : Result.Success(user))
            .BindAsync(user => authProviderRepository
                .GetByIdAsync(user.AuthProviderId!.Value, cancellationToken)
                .BindAsync(provider => provider is null
                    ? Result.Fail<ZipResult<User, AuthProvider>>("Auth provider not found", ErrorCodes.InvalidCredentials)
                    : Result.Success(new ZipResult<User, AuthProvider>(user, provider))))
            .BindAsync((_, provider) =>
            {
                var authenticator = authenticators.FirstOrDefault(a => a.ProviderType == provider.Provider);
                if (authenticator is null)
                {
                    return Result.FailAsync<User>(
                        $"No authenticator found for provider type '{provider.Provider}'",
                        ErrorCodes.InvalidCredentials);
                }
                return authenticator.AuthenticateAsync(username, password, provider, cancellationToken);
            })
            .MapAsync<User, ClaimsPrincipal?>(CreatePrincipal)
            .ConfigureAwait(false);
    }

    public void Challenge(HttpContext context)
    {
        context.Response.Headers[HeaderNames.WWWAuthenticate] = "Basic realm=\"Magpie\"";
    }

    private static ClaimsPrincipal CreatePrincipal(User user)
    {
        var identity = new ClaimsIdentity(ProviderTypeName);

        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Username));
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.AuthenticationMethod, ProviderTypeName));

        if (user.AuthProviderId is not null)
        {
            identity.AddClaim(new Claim(MagpieClaims.AuthProviderId, user.AuthProviderId.Value.ToString()));
        }
        return new ClaimsPrincipal(identity);
    }
}
