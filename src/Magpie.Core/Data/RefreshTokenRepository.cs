using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Data.Database.Entities;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using static Magpie.Abstractions.Data.IRefreshTokenRepository;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing refresh tokens.
/// </summary>
public sealed class RefreshTokenRepository(
    MagpieDbContext context,
    ILogger<RefreshTokenRepository> logger) : IRefreshTokenRepository
{
    /// <inheritdoc />
    public async Task<Result<RefreshTokenInfo>> CreateAsync(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = new RefreshToken
            {
                UserId = userId,
                TokenHash = tokenHash,
                ExpiresAt = expiresAt,
                CreatedAt = DateTimeOffset.UtcNow
            };

            context.RefreshTokens.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(new RefreshTokenInfo(entity.UserId, entity.ExpiresAt));
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error creating refresh token for user {UserId} ({ExErrorCode})",
                userId,
                errorCode ?? "");
            return Result.Fail<RefreshTokenInfo>(
                "Error creating refresh token",
                ErrorCodes.DbRefreshTokenError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<RefreshTokenInfo?>> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await (
                from rt in context.RefreshTokens.AsNoTracking()
                where rt.TokenHash == tokenHash
                select new { rt.UserId, rt.ExpiresAt })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success<RefreshTokenInfo?>(null);
            }

            return Result.Success<RefreshTokenInfo?>(new(entity.UserId, entity.ExpiresAt));
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error retrieving refresh token ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<RefreshTokenInfo?>(
                "Error retrieving refresh token",
                ErrorCodes.DbRefreshTokenError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> DeleteAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await context.RefreshTokens
                .Where(rt => rt.TokenHash == tokenHash)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(deleted > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error deleting refresh token ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<bool>("Error deleting refresh token", ErrorCodes.DbRefreshTokenError, errorCode);
        }
    }
}
