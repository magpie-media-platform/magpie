using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Security;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class AuthEndpointGroup : IApiEndpointGroup
{
    private const string RefreshCookieName = "magpie.refresh";
    private const string RefreshCookiePath = "/api/auth";

    public string Name => "auth";

    private static void SetRefreshCookie(HttpContext httpContext, AuthSession session)
    {
        httpContext.Response.Cookies.Append(
            RefreshCookieName,
            session.RefreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Path = RefreshCookiePath,
                Expires = session.RefreshTokenExpiresAt,
                IsEssential = true,
            });
    }

    private static void ClearRefreshCookie(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(
            RefreshCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Path = RefreshCookiePath,
            });
    }

    private static void SetStreamAuthCookie(HttpContext httpContext, AuthSession session)
    {
        httpContext.Response.Cookies.Append(
            StreamAuthentication.CookieName,
            session.AccessToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = StreamAuthentication.Path,
                Expires = session.AccessTokenExpiresAt,
                IsEssential = true,
            });
    }

    private static void ClearStreamAuthCookie(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(
            StreamAuthentication.CookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = StreamAuthentication.Path,
            });
    }

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapPost("login", AuthLoginAsync)
            .WithName(nameof(AuthLoginAsync))
            .Produces<AuthTokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .AllowAnonymous();

        builder.MapPost("logout", AuthLogoutAsync)
            .WithName(nameof(AuthLogoutAsync))
            .Produces(StatusCodes.Status204NoContent)
            .RequireViewer();

        builder.MapPost("refresh", AuthRefreshAsync)
            .WithName(nameof(AuthRefreshAsync))
            .Produces<AuthTokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .AllowAnonymous();

        builder.MapGet("permissions", GetPermissions)
            .WithName(nameof(GetPermissions))
            .Produces<EffectivePermissions>()
            .RequireViewer();
    }

    internal async Task<IResult> AuthLoginAsync(
        HttpContext httpContext,
        [FromServices] IAuthService authService,
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var session = await authService.LoginAsync(request.Username, request.Password, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return Results.Problem(
                title: "Invalid credentials",
                detail: "The username or password is incorrect, or the account is disabled.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        SetRefreshCookie(httpContext, session);
        SetStreamAuthCookie(httpContext, session);

        return Results.Ok(new AuthTokenResponse
        {
            AccessToken = session.AccessToken,
            AccessTokenExpiresAt = session.AccessTokenExpiresAt,
        });
    }

    internal async Task<IResult> AuthLogoutAsync(
        HttpContext httpContext,
        [FromServices] IAuthService authService,
        CancellationToken cancellationToken)
    {
        var refreshToken = httpContext.Request.Cookies[RefreshCookieName];

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await authService.LogoutAsync(refreshToken, cancellationToken).ConfigureAwait(false);
        }

        ClearRefreshCookie(httpContext);
        ClearStreamAuthCookie(httpContext);

        return Results.NoContent();
    }

    internal async Task<IResult> AuthRefreshAsync(
        HttpContext httpContext,
        [FromServices] IAuthService authService,
        CancellationToken cancellationToken)
    {
        var refreshToken = httpContext.Request.Cookies[RefreshCookieName];

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            ClearRefreshCookie(httpContext);
            ClearStreamAuthCookie(httpContext);
            return Results.Problem(
                title: "Missing refresh token",
                detail: "The session expired. Sign in again.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var session = await authService.RefreshAsync(refreshToken, cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            ClearRefreshCookie(httpContext);
            ClearStreamAuthCookie(httpContext);
            return Results.Problem(
                title: "Invalid refresh token",
                detail: "The refresh token is missing, expired, or already used.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        SetRefreshCookie(httpContext, session);
        SetStreamAuthCookie(httpContext, session);

        return Results.Ok(new AuthTokenResponse
        {
            AccessToken = session.AccessToken,
            AccessTokenExpiresAt = session.AccessTokenExpiresAt,
        });
    }

    internal IResult GetPermissions(
        [FromServices] IAuthService authService,
        AuthenticatedUser user)
    {
        return Results.Ok(authService.GetEffectivePermissions(user.Role));
    }
}
