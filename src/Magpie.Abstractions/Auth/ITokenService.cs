using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Models.Security;

namespace Magpie.Abstractions.Auth;

/// <summary>
/// The service contract for access tokens and refresh tokens.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Creates an access token and the identifier that its refresh token must bind to.
    /// </summary>
    /// <param name="user">The user that the token identifies</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IssuedAccessToken> GenerateAccessTokenAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a refresh token bound to an access token identifier.
    /// </summary>
    /// <param name="userId">The id of the user that the token identifies</param>
    /// <param name="accessTokenJti">The identifier returned with the associated access token</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IssuedToken> GenerateRefreshTokenAsync(
        Guid userId,
        string accessTokenJti,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a refresh token and gets its user id, or <see langword="null"/> when the token is invalid.
    /// </summary>
    /// <param name="refreshToken">The refresh token to validate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Guid?> TryValidateRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems a single-use refresh token and gets its user id, or <see langword="null"/> when the token is invalid.
    /// </summary>
    /// <param name="refreshToken">The refresh token to redeem</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Guid?> TryConsumeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token to revoke</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an access token and gets its principal, or <see langword="null"/> when the token is invalid.
    /// </summary>
    /// <param name="accessToken">The access token to validate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ClaimsPrincipal?> TryValidateAccessTokenAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}
