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
    /// Gets the current token for a committed renewal with matching credentials.
    /// </summary>
    /// <param name="tokenHash">The previous or current refresh token hash</param>
    /// <param name="renewalKeyHash">The client renewal proof hash</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<RefreshToken?> GetRenewalAsync(
        string tokenHash, string renewalKeyHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the recovery receipt on the current refresh token.
    /// </summary>
    /// <param name="token">The current token and its recovery fields</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task StoreRenewalAsync(RefreshToken token, CancellationToken cancellationToken = default);

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
    /// Gets a refresh token by its associated access token identifier.
    /// </summary>
    /// <param name="jti">The access token identifier to find</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The refresh token, or <see langword="null"/> when no token matches</returns>
    Task<RefreshToken?> GetByAccessTokenJtiAsync(string jti, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems a single-use refresh token that is valid and not expired.
    /// </summary>
    /// <param name="tokenHash">The hash of the refresh token to redeem</param>
    /// <param name="now">The current time that the expiry test uses</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The redeemed token, or <c>null</c> when no token matches or the token is expired</returns>
    Task<RefreshToken?> ConsumeAsync(string tokenHash, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all the refresh tokens of a user.
    /// </summary>
    /// <param name="userId">The id of the user whose tokens the operation deletes</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The count of the deleted tokens</returns>
    Task<int> DeleteByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a refresh token by its hash.
    /// </summary>
    /// <param name="tokenHash">The hash of the refresh token to delete</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the token exists, otherwise <see langword="false"/></returns>
    Task<bool> DeleteAsync(string tokenHash, CancellationToken cancellationToken = default);
}
