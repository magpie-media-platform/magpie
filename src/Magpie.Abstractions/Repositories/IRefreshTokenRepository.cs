using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing refresh tokens.
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>
    /// Create a new refresh token.
    /// </summary>
    /// <param name="token">The refresh token to create.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a refresh token by its hash.
    /// </summary>
    /// <param name="tokenHash">The token hash.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a refresh token by its hash.
    /// </summary>
    /// <param name="tokenHash">The token hash.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<bool> DeleteAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all refresh tokens for a user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<int> DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all expired refresh tokens.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default);
}
