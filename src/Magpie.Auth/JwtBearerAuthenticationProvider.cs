using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Sdk.Api;
using Magpie.Sdk.Auth;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Magpie.Auth;

internal sealed class JwtBearerAuthenticationProvider(ITokenService tokenService) : IAuthenticationProvider
{
    private static readonly PathString StreamPathSegment = new(StreamAuthentication.Path);
    public const string BearerProviderType = "bearer";

    private const string BearerSchemePrefix = "Bearer ";

    public string ProviderType => BearerProviderType;

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

    private static string? ExtractStreamCookieToken(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(StreamPathSegment, StringComparison.Ordinal))
        {
            return null;
        }

        var cookie = context.Request.Cookies[
            AuthenticationCookies.GetName(StreamAuthentication.CookieName, context.Request)]
            ?? context.Request.Cookies[StreamAuthentication.CookieName];
        return string.IsNullOrWhiteSpace(cookie) ? null : cookie;
    }

    public async Task<AuthenticationAttempt> TryAuthenticateAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? token = null;
        if (context.Request.Headers.TryGetValue(HeaderNames.Authorization, out var values))
        {
            token = ExtractBearerToken(values);
        }

        token ??= ExtractStreamCookieToken(context);

        if (token is null)
        {
            return AuthenticationAttempt.None;
        }

        var principal = await tokenService.TryValidateAccessTokenAsync(token, cancellationToken)
            .ConfigureAwait(false);

        return principal is null
            ? AuthenticationAttempt.Rejected("the access token is invalid or expired")
            : AuthenticationAttempt.Authenticated(principal);
    }

    public Task ChallengeAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Response.Headers.ContainsKey(HeaderNames.WWWAuthenticate))
        {
            context.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
        }

        return Task.CompletedTask;
    }
}
