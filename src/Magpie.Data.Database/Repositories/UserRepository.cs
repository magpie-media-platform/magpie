using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Data.Database.Repositories;

/// <summary>
/// Repository for managing user accounts.
/// </summary>
public sealed class UserRepository(MagpieDbContext context, ILogger<UserRepository> logger) : IUserRepository
{
    /// <inheritdoc />
    public async Task<Result<User?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var user = await (
                from u in context.Users.Include(u => u.ProviderData)
                where u.Id == id
                select u)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(user?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting user {UserId} ({ExErrorCode})", id, errorCode ?? "");
            return Result.Fail<User?>("Error retrieving user", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<User?>> GetByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        try
        {
            var user = await (
                from u in context.Users.Include(u => u.ProviderData)
                where u.Username == username
                select u)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(user?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting user {Username} ({ExErrorCode})", username, errorCode ?? "");
            return Result.Fail<User?>("Error retrieving user", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<User?>> GetByExternalIdAsync(
        Guid authProviderId,
        string externalId,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await (
                from u in context.Users.Include(u => u.ProviderData)
                where u.AuthProviderId == authProviderId
                where u.ExternalId == externalId
                select u)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(user?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting user {ExternalId} with auth provider {AuthProviderId} ({ExErrorCode})",
                externalId,
                authProviderId,
                errorCode ?? "");
            return Result.Fail<User?>("Error retrieving user", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<User>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var users = await (
                from u in context.Users.Include(u => u.ProviderData)
                select u)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = users.Select(u => u.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<User>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting users ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<User>>("Error retrieving users", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<User>> CreateAsync(
        User model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var authProvider = await context.AuthProviders
                .FindAsync([model.AuthProviderId], cancellationToken)
                .ConfigureAwait(false);

            if (authProvider == null)
            {
                return Result.Fail<User>("Auth provider not found.", ErrorCodes.DbAuthProviderError);
            }

            var entity = model.ToEntity(authProvider, createdByUserId);

            context.Users.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success<User>(entity.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error creating user {Username} ({ExErrorCode})", model.Username, errorCode ?? "");
            return Result.Fail<User>("Error creating user", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        User model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.Users
                .Where(u => u.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Username, model.Username)
                        .SetProperty(e => e.Email, model.Email)
                        .SetProperty(e => e.Enabled, model.Enabled)
                        .SetProperty(e => e.Role, model.Role)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error updating user {UserId} ({ExErrorCode})", model.Id, errorCode ?? "");
            return Result.Fail<bool>("Error saving user", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        UserPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.Users
                .Where(u => u.Id == patch.Id)
                .ExecuteUpdateAsync(setter =>
                {
                    if (patch.Enabled != null)
                    {
                        setter.SetProperty(e => e.Enabled, patch.Enabled.Value);
                    }
                    if (patch.Role != null)
                    {
                        setter.SetProperty(e => e.Role, patch.Role.Value);
                    }
                    setter.SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow);
                    setter.SetProperty(e => e.ModifiedByUserId, modifiedByUserId);
                }, cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error patching user {UserId} ({ExErrorCode})", patch.Id, errorCode ?? "");
            return Result.Fail<bool>("Error saving user", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result> UpdatePasswordAsync(
        Guid id,
        string passwordHash,
        string passwordSalt,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.Users
                .Where(u => u.Id == id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.PasswordHash, passwordHash)
                        .SetProperty(e => e.PasswordSalt, passwordSalt)
                        .SetProperty(e => e.FailedLoginAttempts, 0)
                        .SetProperty(e => e.LockedUntil, default(DateTimeOffset?))
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error updating password for user {UserId} ({ExErrorCode})", id, errorCode ?? "");
            return Result.Fail("Error updating password", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result> RecordFailedLoginAsync(
        Guid id,
        TimeSpan? lockoutDuration,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.Users
                .Where(u => u.Id == id)
                .ExecuteUpdateAsync(setter =>
                {
                    if (lockoutDuration is not null)
                    {
                        setter.SetProperty(e => e.LockedUntil, DateTimeOffset.UtcNow.Add(lockoutDuration.Value));
                    }
                    setter.SetProperty(e => e.FailedLoginAttempts, e => e.FailedLoginAttempts + 1);
                    setter.SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow);
                }, cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error recording failed login for user {UserId} ({ExErrorCode})", id, errorCode ?? "");
            return Result.Fail("Error recording failed login", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result> RecordSuccessfulLoginAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.Users
                .Where(u => u.Id == id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.FailedLoginAttempts, 0)
                        .SetProperty(e => e.LockedUntil, default(DateTimeOffset?))
                        .SetProperty(e => e.LastLoginAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error recording login for user {UserId} ({ExErrorCode})", id, errorCode ?? "");
            return Result.Fail("Error recording login", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await context.Users
                .Where(u => u.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(deleted > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error deleting user {UserId} ({ExErrorCode})", id, errorCode ?? "");
            return Result.Fail<bool>("Error deleting user", ErrorCodes.DbUserError, errorCode);
        }
    }
}
