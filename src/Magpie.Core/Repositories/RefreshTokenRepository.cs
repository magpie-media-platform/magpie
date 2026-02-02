using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

/// <summary>
/// Repository for managing refresh tokens.
/// </summary>
public sealed class RefreshTokenRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<RefreshTokenRepository> logger) : IRefreshTokenRepository
{
    /// <inheritdoc />
    public async Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                token.CreatedAt = now;

                context.RefreshTokens.Add(token);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.Security,
                    Operation = "Create",
                    EntityId = token.Id.ToString(),
                    ParentEntityId = token.UserId.ToString(),
                    EntityType = nameof(RefreshToken),
                    Detail = $"Created refresh token for user {token.UserId}",
                    Timestamp = now,
                    UserId = token.UserId
                }, cancellationToken).ConfigureAwait(false);

                return token;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating refresh token for user {UserId}", token.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.RefreshTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving refresh token");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var token = await context.RefreshTokens
                    .AsNoTracking()
                    .Where(rt => rt.TokenHash == tokenHash)
                    .Select(rt => new { rt.Id, rt.UserId })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (token is null)
                {
                    return false;
                }

                var deleted = await context.RefreshTokens
                    .Where(rt => rt.TokenHash == tokenHash)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Security,
                        Operation = "Delete",
                        EntityId = token.Id.ToString(),
                        ParentEntityId = token.UserId.ToString(),
                        EntityType = nameof(RefreshToken),
                        Detail = $"Deleted refresh token for user {token.UserId}",
                        Timestamp = DateTime.UtcNow,
                        UserId = token.UserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting refresh token");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var deleted = await context.RefreshTokens
                    .Where(rt => rt.UserId == userId)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Security,
                        Operation = "DeleteAll",
                        EntityId = userId.ToString(),
                        EntityType = nameof(RefreshToken),
                        Detail = $"Deleted {deleted} refresh tokens for user {userId}",
                        Timestamp = DateTime.UtcNow,
                        UserId = userId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting refresh tokens for user {UserId}", userId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await context.RefreshTokens
                .Where(rt => rt.ExpiresAt < DateTime.UtcNow)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.Security,
                    Operation = "DeleteExpired",
                    EntityId = string.Empty,
                    EntityType = nameof(RefreshToken),
                    Detail = $"Deleted {deleted} expired refresh tokens",
                    Timestamp = DateTime.UtcNow,
                    UserId = null
                }, cancellationToken).ConfigureAwait(false);
            }

            return deleted;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting expired refresh tokens");
            throw;
        }
    }
}
