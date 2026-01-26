using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Models.History;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using UserEntity = Magpie.Data.Database.Entities.User;
using UserProviderDataEntity = Magpie.Data.Database.Entities.UserProviderData;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing user accounts and provider data.
/// </summary>
public sealed class UserRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<UserRepository> logger) : IUserRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<User>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var users = await context.Users
                .AsNoTracking()
                .Include(u => u.ProviderData)
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
    public async Task<Result<User?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var user = await (
                from u in context.Users.AsNoTracking().Include(u => u.ProviderData)
                where u.Id == id
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
                "Error getting user {UserId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<User?>("Error retrieving user", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<User?>> GetByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        try
        {
            var user = await (
                from u in context.Users.AsNoTracking().Include(u => u.ProviderData)
                where u.Username == username
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
                "Error getting user {Username} ({ExErrorCode})",
                username,
                errorCode ?? "");
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
                from u in context.Users.AsNoTracking().Include(u => u.ProviderData)
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
    public async Task<Result<UserCredentials?>> GetUserCredentialsAsync(
        string username,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from u in context.Users.AsNoTracking()
                where u.Username == username
                select u)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToCredentialsModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting credentials for user {Username} ({ExErrorCode})",
                username,
                errorCode ?? "");
            return Result.Fail<UserCredentials?>("Error retrieving credentials", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<User>> CreateAsync(
        string username,
        string email,
        Guid authProviderId,
        string? externalId,
        bool enabled,
        Role role,
        string? passwordHash,
        string? passwordSalt,
        IReadOnlyDictionary<string, string> providerData,
        DateTimeOffset createdAt,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = new UserEntity
            {
                Id = Guid.NewGuid(),
                Username = username,
                Email = email,
                AuthProviderId = authProviderId == Guid.Empty ? null : authProviderId,
                ExternalId = externalId,
                Enabled = enabled,
                Role = role,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                CreatedAt = createdAt,
                CreatedByUserId = createdByUserId
            };

            foreach (var kvp in providerData)
            {
                entity.ProviderData.Add(new UserProviderDataEntity
                {
                    Key = kvp.Key,
                    Value = kvp.Value
                });
            }

            context.Users.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Security,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(User),
                description: $"Created user {username}",
                userId: createdByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(entity.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error creating user {Username} ({ExErrorCode})",
                username,
                errorCode ?? "");
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
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.Users
                .Where(u => u.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Username, model.Username)
                        .SetProperty(e => e.Email, model.Email)
                        .SetProperty(e => e.AuthProviderId, model.AuthProviderId)
                        .SetProperty(e => e.ExternalId, model.ExternalId)
                        .SetProperty(e => e.Enabled, model.Enabled)
                        .SetProperty(e => e.Role, model.Role)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0 && model.ProviderData is not null)
            {
                var existingKeys = await context.UserProviderData
                    .Where(pd => pd.UserId == model.Id)
                    .Select(pd => pd.Key)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                await context.UserProviderData
                    .Where(pd => pd.UserId == model.Id)
                    .Where(pd => !model.ProviderData.Keys.Contains(pd.Key))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var kvp in model.ProviderData)
                {
                    if (existingKeys.Contains(kvp.Key))
                    {
                        await context.UserProviderData
                            .Where(pd => pd.UserId == model.Id)
                            .Where(pd => pd.Key == kvp.Key)
                            .ExecuteUpdateAsync(
                                setter => setter.SetProperty(e => e.Value, kvp.Value),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        context.UserProviderData.Add(new UserProviderDataEntity
                        {
                            UserId = model.Id,
                            Key = kvp.Key,
                            Value = kvp.Value
                        });
                    }
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            if (updated > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Security,
                    operation: "Update",
                    entityId: model.Id,
                    entityType: nameof(User),
                    description: $"Updated user {model.Username}",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error updating user {UserId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>("Error updating user", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> DeleteAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (user is null)
            {
                return Result.Success(false);
            }

            await context.UserProviderData
                .Where(pd => pd.UserId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.Users
                .Where(u => u.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Security,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(User),
                    description: $"Deleted user {user.Username}",
                    userId: deletedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(deleted > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error deleting user {UserId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>("Error deleting user", ErrorCodes.DbUserError, errorCode);
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
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Id == patch.Id, cancellationToken)
                .ConfigureAwait(false);

            if (user is null)
            {
                return Result.Success(false);
            }

            if (patch.Enabled is not null)
            {
                user.Enabled = patch.Enabled.Value;
            }

            if (patch.Role is not null)
            {
                user.Role = patch.Role.Value;
            }

            user.ModifiedAt = DateTimeOffset.UtcNow;
            user.ModifiedByUserId = modifiedByUserId;

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (updated > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Security,
                    operation: "Patch",
                    entityId: patch.Id,
                    entityType: nameof(User),
                    description: $"Patched user {user.Username}",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error patching user {UserId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>("Error patching user", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Unit>> UpdatePasswordAsync(
        Guid id,
        string? passwordHash,
        string? passwordSalt,
        bool? unlock,
        bool? resetFailedLoginAttempts,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (user is null)
            {
                return Result.Success();
            }

            if (passwordHash is not null)
            {
                user.PasswordHash = passwordHash;
            }

            if (passwordSalt is not null)
            {
                user.PasswordSalt = passwordSalt;
            }

            if (unlock == true)
            {
                user.LockedUntil = null;
            }

            if (resetFailedLoginAttempts == true)
            {
                user.FailedLoginAttempts = 0;
            }

            user.ModifiedAt = DateTimeOffset.UtcNow;
            user.ModifiedByUserId = modifiedByUserId;

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Security,
                operation: "UpdatePassword",
                entityId: id,
                entityType: nameof(User),
                description: "Updated user password",
                userId: modifiedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success();
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error updating password for user {UserId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<Unit>("Error updating password", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<User>> UnlockUserAsync(
        Guid id,
        bool? resetFailedLoginAttempts,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var user = await context.Users
                .Include(u => u.ProviderData)
                .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (user is null)
            {
                return Result.Fail<User>("User not found", ErrorCodes.DbUserError);
            }

            user.LockedUntil = null;

            if (resetFailedLoginAttempts == true)
            {
                user.FailedLoginAttempts = 0;
            }

            user.ModifiedAt = DateTimeOffset.UtcNow;
            user.ModifiedByUserId = modifiedByUserId;

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Security,
                operation: "UnlockUser",
                entityId: id,
                entityType: nameof(User),
                description: $"Unlocked user {user.Username}",
                userId: modifiedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(user.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error unlocking user {UserId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<User>("Error unlocking user", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Unit>> RecordFailedLoginAsync(
        Guid id,
        TimeSpan? lockoutDuration,
        CancellationToken cancellationToken)
    {
        try
        {
            var lockedUntil = lockoutDuration.HasValue
                ? DateTimeOffset.UtcNow.Add(lockoutDuration.Value)
                : (DateTimeOffset?)null;

            await context.Users
                .Where(u => u.Id == id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.FailedLoginAttempts, e => e.FailedLoginAttempts + 1)
                        .SetProperty(e => e.LockedUntil, lockedUntil),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result.Success();
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error recording failed login for user {UserId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<Unit>("Error recording failed login", ErrorCodes.DbUserError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Unit>> ResetFailedLoginAttemptsAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await context.Users
                .Where(u => u.Id == id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.FailedLoginAttempts, 0)
                        .SetProperty(e => e.LastLoginAt, DateTimeOffset.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result.Success();
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error resetting failed login attempts for user {UserId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<Unit>("Error resetting failed login attempts", ErrorCodes.DbUserError, errorCode);
        }
    }
}
