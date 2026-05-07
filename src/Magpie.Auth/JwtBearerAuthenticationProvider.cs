using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Magpie.Auth;

/// <summary>
/// Authenticates requests that carry a JWT access token in the
/// <c>Authorization: Bearer &lt;token&gt;</c> header.
/// </summary>
/// <remarks>
/// Delegates token validation to <see cref="ITokenService.TryValidateAccessTokenAsync"/>.
/// Returns <see langword="null"/> when no Authorization header is present, when the
/// scheme is not <c>Bearer</c>, or when the token fails validation, allowing the
/// authentication handler to fall through to other providers.
/// </remarks>
internal sealed class JwtBearerAuthenticationProvider(
    ITokenService tokenService,
    ILogger<JwtBearerAuthenticationProvider> logger) : IAuthenticationProvider
{
    /// <summary>
    /// The provider type identifier for JWT bearer authentication.
    /// </summary>
    public const string BearerProviderType = "bearer";

    private const string BearerSchemePrefix = "Bearer ";

    /// <inheritdoc />
    public string ProviderType => BearerProviderType;

    /// <inheritdoc />
    public async Task<ClaimsPrincipal?> TryAuthenticateAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Headers.TryGetValue(HeaderNames.Authorization, out var values))
        {
            return null;
        }

        var token = ExtractBearerToken(values);
        if (token is null)
        {
            return null;
        }

        var principal = await tokenService.TryValidateAccessTokenAsync(token, cancellationToken)
            .ConfigureAwait(false);

        if (principal is null)
        {
            logger.LogDebug("Bearer token validation failed");
            return null;
        }

        return principal;
    }

    /// <inheritdoc />
    public Task ChallengeAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Advertise bearer authentication on 401 responses; do not overwrite an existing header.
        if (!context.Response.Headers.ContainsKey(HeaderNames.WWWAuthenticate))
        {
            context.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
        }

        return Task.CompletedTask;
    }

    private static string? ExtractBearerToken(StringValues values)
    {
        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (!raw.StartsWith(BearerSchemePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var token = raw.AsSpan(BearerSchemePrefix.Length).Trim();
            if (token.Length == 0)
            {
                continue;
            }

            return token.ToString();
        }

        return null;
    }
}
