using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Repositories;
using Magpie.Models.Api;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class AuthEndpointGroup(
    ICredentialAuthenticationProvider authProvider,
    ITokenService tokenService,
    IUserRepository userRepository) : IApiEndpointGroup
{
    public string Name => "auth";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapPost("login", AuthLogin)
            .WithName(nameof(AuthLogin))
            .Produces<AuthTokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AllowAnonymous();

        builder.MapPost("logout", AuthLogout)
            .WithName(nameof(AuthLogout))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        builder.MapPost("refresh", AuthRefresh)
            .WithName(nameof(AuthRefresh))
            .Produces<AuthTokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AllowAnonymous();
    }

    private async Task<IResult> AuthLogin(LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await authProvider.VerifyCredentialsAsync(request.Username, request.Password, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var accessToken = await tokenService.GenerateAccessTokenAsync(user, cancellationToken).ConfigureAwait(false);
        var refreshToken = await tokenService.GenerateRefreshTokenAsync(user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new AuthTokenResponse
        {
            AccessToken = accessToken.Token,
            AccessTokenExpiresAt = accessToken.ExpiresAt,
            RefreshToken = refreshToken
        });
    }

    private async Task<IResult> AuthLogout(LogoutRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
        }

        await tokenService.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken).ConfigureAwait(false);

        return Results.NoContent();
    }

    private async Task<IResult> AuthRefresh(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
        }

        var userId = await tokenService.TryValidateRefreshTokenAsync(request.RefreshToken, cancellationToken)
            .ConfigureAwait(false);

        if (userId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var user = await userRepository.GetByIdAsync(userId.Value, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        await tokenService.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken).ConfigureAwait(false);

        var accessToken = await tokenService.GenerateAccessTokenAsync(user, cancellationToken).ConfigureAwait(false);
        var refreshToken = await tokenService.GenerateRefreshTokenAsync(user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new AuthTokenResponse
        {
            AccessToken = accessToken.Token,
            AccessTokenExpiresAt = accessToken.ExpiresAt,
            RefreshToken = refreshToken
        });
    }
}
