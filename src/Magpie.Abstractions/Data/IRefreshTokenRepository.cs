using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing refresh tokens. CRUD only, no business logic.
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>
    /// Record type for a refresh token.
    /// </summary>
    public record RefreshTokenInfo(Guid UserId, DateTimeOffset ExpiresAt);

    /// <summary>
    /// Creates a new refresh token.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="tokenHash">The SHA-256 hash of the token.</param>
    /// <param name="expiresAt">When the token expires.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<RefreshTokenInfo>> CreateAsync(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a refresh token by its hash, returning userId and expiration.
    /// </summary>
    /// <param name="tokenHash">The SHA-256 hash of the token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<RefreshTokenInfo?>> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a refresh token by its hash.
    /// </summary>
    /// <param name="tokenHash">The SHA-256 hash of the token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<bool>> DeleteAsync(string tokenHash, CancellationToken cancellationToken = default);
}
