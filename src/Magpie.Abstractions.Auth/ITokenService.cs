using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;
using Magpie.Models.Users;

namespace Magpie.Abstractions.Auth;

/// <summary>
/// Service for JWT token generation and validation.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Result of generating an access token.
    /// </summary>
    /// <param name="Token">The JWT access token string.</param>
    /// <param name="ExpiresAt">When the token expires.</param>
    public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

    /// <summary>
    /// Generate an access token (JWT) for a user.
    /// </summary>
    /// <param name="user">The user to generate a token for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The access token and its expiration time.</returns>
    Task<Result<AccessToken>> GenerateAccessTokenAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a refresh token.
    /// </summary>
    /// <param name="userId">The user ID to associate with the token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The refresh token.</returns>
    Task<Result<string>> GenerateRefreshTokenAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate a refresh token and return the associated user ID.
    /// </summary>
    /// <param name="refreshToken">The refresh token to validate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The user ID if the token is valid.</returns>
    Task<Result<Guid>> ValidateRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke a refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token to revoke.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<Unit>> RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate an access token and extract the claims principal.
    /// </summary>
    /// <param name="accessToken">The access token to validate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The claims principal if the token is valid.</returns>
    Task<Result<ClaimsPrincipal>> ValidateAccessTokenAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}
