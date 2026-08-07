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

internal sealed class RefreshTokenRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<RefreshTokenRepository> logger,
    TimeProvider? timeProvider = null) : IRefreshTokenRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    token.CreatedAt = now;

                    context.RefreshTokens.Add(token);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Security,
                            Operation = "Create",
                            EntityId = token.Id.ToString(),
                            ParentEntityId = token.UserId.ToString(),
                            EntityType = nameof(RefreshToken),
                            Detail = $"Created refresh token for user {token.UserId}",
                            Timestamp = now,
                            UserId = token.UserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return token;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The refresh token repository failed to create a refresh token for user " +
                "{UserId}.", token.UserId);
            throw;
        }
    }

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
            logger.LogError(ex, "The refresh token repository failed to read the refresh token.");
            throw;
        }
    }

    public async Task<RefreshToken?> ConsumeAsync(
        string tokenHash,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var token = await context.RefreshTokens
                        .AsNoTracking()
                        .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash && rt.ExpiresAt > now, cancellationToken)
                        .ConfigureAwait(false);

                    if (token is null)
                    {
                        return null;
                    }

                    var deleted = await context.RefreshTokens
                        .Where(rt => rt.TokenHash == tokenHash)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (deleted == 0)
                    {
                        return null;
                    }

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Security,
                            Operation = "Consume",
                            EntityId = token.Id.ToString(),
                            ParentEntityId = token.UserId.ToString(),
                            EntityType = nameof(RefreshToken),
                            Detail = $"Consumed refresh token for user {token.UserId}",
                            Timestamp = _time.GetUtcNow().UtcDateTime,
                            UserId = token.UserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return token;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The refresh token repository failed to consume the refresh token.");
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var token = await (
                        from rt in context.RefreshTokens.AsNoTracking()
                        where rt.TokenHash == tokenHash
                        select new { rt.Id, rt.UserId })
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
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Security,
                                Operation = "Delete",
                                EntityId = token.Id.ToString(),
                                ParentEntityId = token.UserId.ToString(),
                                EntityType = nameof(RefreshToken),
                                Detail = $"Deleted refresh token for user {token.UserId}",
                                Timestamp = _time.GetUtcNow().UtcDateTime,
                                UserId = token.UserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return deleted > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The refresh token repository failed to delete the refresh token.");
            throw;
        }
    }
}
