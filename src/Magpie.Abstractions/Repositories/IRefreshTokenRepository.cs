using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for refresh tokens.
/// </summary>
public interface IRefreshTokenRepository : IRepository
{
    /// <summary>
    /// Creates a new refresh token.
    /// </summary>
    /// <param name="token">The refresh token to create</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The new refresh token</returns>
    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a refresh token by its hash.
    /// </summary>
    /// <param name="tokenHash">The hash of the refresh token to find</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The refresh token, or <c>null</c> when no token matches</returns>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems a single-use refresh token that is valid and not expired.
    /// </summary>
    /// <param name="tokenHash">The hash of the refresh token to redeem</param>
    /// <param name="now">The current time that the expiry test uses</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The redeemed token, or <c>null</c> when no token matches or the token is expired</returns>
    Task<RefreshToken?> ConsumeAsync(string tokenHash, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a refresh token by its hash.
    /// </summary>
    /// <param name="tokenHash">The hash of the refresh token to delete</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the token exists, otherwise <see langword="false"/></returns>
    Task<bool> DeleteAsync(string tokenHash, CancellationToken cancellationToken = default);
}
