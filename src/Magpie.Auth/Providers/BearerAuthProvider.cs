using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Core;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Magpie.Auth.Providers;

internal sealed class BearerAuthProvider(ITokenService tokenService) : IAuthenticationProvider
{
    public const string ProviderTypeName = "bearer";

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

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success<ClaimsPrincipal?>(null);
        }

        var token = authHeader["Bearer ".Length..].Trim();

        if (string.IsNullOrEmpty(token))
        {
            return Result.Fail<ClaimsPrincipal?>("Bearer token is empty", ErrorCodes.TokenValidationError);
        }

        return await tokenService.ValidateAccessTokenAsync(token, cancellationToken)
            .MapAsync<ClaimsPrincipal, ClaimsPrincipal?>(principal => principal)
            .ConfigureAwait(false);
    }

    public void Challenge(HttpContext context)
    {
        context.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
    }
}
