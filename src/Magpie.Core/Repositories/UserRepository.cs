using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Models.Repositories;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class UserRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<UserRepository> logger,
    TimeProvider? timeProvider = null) : IUserRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private static IReadOnlyCollection<string> ChangedFields(UserPatch patch)
    {
        var fields = new List<string>();
        if (patch.Enabled is not null)
        {
            fields.Add("enabled");
        }

        if (patch.Role is not null)
        {
            fields.Add("role");
        }

        return fields;
    }

    public async Task<IEnumerable<User>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.Users
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The user repository failed to list users.");
            throw;
        }
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The user repository failed to read user {UserId}.", id);
            throw;
        }
    }

    public async Task<User> CreateAsync(User model, Guid? createdByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var created = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    model.CreatedAt = now;
                    model.CreatedByUserId = createdByUserId;

                    context.Users.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Security,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(User),
                            Detail = $"Created user {model.Username}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    SecurityEventBuilder.BuildUserCreatedEvent(created.Id, created.Username, created.Role.ToString()),
                    token),
                cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The user repository failed to create user {Username}.", model.Username);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(User model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var updated = await context.Users
                        .Where(u => u.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(u => u.Username, model.Username)
                                .SetProperty(u => u.Email, model.Email)
                                .SetProperty(u => u.ExtensionId, model.ExtensionId)
                                .SetProperty(u => u.ProviderType, model.ProviderType)
                                .SetProperty(u => u.ExternalId, model.ExternalId)
                                .SetProperty(u => u.Settings, model.Settings)
                                .SetProperty(u => u.Enabled, model.Enabled)
                                .SetProperty(u => u.Role, model.Role)
                                .SetProperty(u => u.PasswordHash, model.PasswordHash)
                                .SetProperty(u => u.PasswordSalt, model.PasswordSalt)
                                .SetProperty(u => u.FailedLoginAttempts, model.FailedLoginAttempts)
                                .SetProperty(u => u.LockedUntil, model.LockedUntil)
                                .SetProperty(u => u.LastLoginAt, model.LastLoginAt)
                                .SetProperty(u => u.ModifiedAt, now)
                                .SetProperty(u => u.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Security,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(User),
                                Detail = $"Updated user {model.Username}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The user repository failed to update user {UserId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var removed = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var model = await (
                        from u in context.Users.AsNoTracking()
                        where u.Id == id
                        select new { u.Username })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var deleted = await context.Users
                        .Where(u => u.Id == id)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (deleted > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Security,
                                Operation = "Delete",
                                EntityId = id.ToString(),
                                EntityType = nameof(User),
                                Detail = $"Deleted user {model.Username}",
                                Timestamp = now,
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return deleted > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (removed)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(SecurityEventBuilder.BuildUserDeletedEvent(id), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The user repository failed to delete user {UserId}.", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(UserPatch patch, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var model = await context.Users
                        .FirstOrDefaultAsync(u => u.Id == patch.Id, cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    if (patch.Enabled is not null)
                    {
                        model.Enabled = patch.Enabled.Value;
                    }

                    if (patch.Role is not null)
                    {
                        model.Role = patch.Role.Value;
                    }

                    model.ModifiedAt = now;
                    model.ModifiedByUserId = modifiedByUserId;

                    var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Security,
                                Operation = "Patch",
                                EntityId = patch.Id.ToString(),
                                EntityType = nameof(User),
                                Detail = $"Updated user {model.Username}.",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (patched)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        SecurityEventBuilder.BuildUserUpdatedEvent(patch.Id, ChangedFields(patch)),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The user repository failed to patch user {UserId}.", patch.Id);
            throw;
        }
    }

    public async Task<bool> SetPasswordAsync(
        Guid userId,
        string passwordHash,
        string passwordSalt,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var updated = await context.Users
                        .Where(u => u.Id == userId)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(u => u.PasswordHash, passwordHash)
                                .SetProperty(u => u.PasswordSalt, passwordSalt)
                                .SetProperty(u => u.FailedLoginAttempts, 0)
                                .SetProperty(u => u.LockedUntil, (DateTime?)null)
                                .SetProperty(u => u.ModifiedAt, now)
                                .SetProperty(u => u.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Security,
                                Operation = "SetPassword",
                                EntityId = userId.ToString(),
                                EntityType = nameof(User),
                                Detail = $"Password updated for user {userId}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The user repository failed to set the password of user {UserId}.", userId);
            throw;
        }
    }

    public async Task<FailedLoginResult> RecordFailedLoginAsync(
        Guid userId,
        DateTime? lockedUntil,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(u => u.FailedLoginAttempts, u => u.FailedLoginAttempts + 1)
                        .SetProperty(u => u.LockedUntil, u => lockedUntil ?? u.LockedUntil),
                    cancellationToken)
                .ConfigureAwait(false);

            var snapshot = await (
                from u in context.Users.AsNoTracking()
                where u.Id == userId
                select new { u.FailedLoginAttempts, u.LockedUntil })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (snapshot is null)
            {
                return new FailedLoginResult(0, null);
            }

            return new FailedLoginResult(snapshot.FailedLoginAttempts, snapshot.LockedUntil);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The user repository failed to record a failed login for user {UserId}.", userId);
            throw;
        }
    }

    public async Task ClearFailedLoginsAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            await context.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(u => u.FailedLoginAttempts, 0)
                        .SetProperty(u => u.LockedUntil, (DateTime?)null)
                        .SetProperty(u => u.LastLoginAt, now),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The user repository failed to clear failed logins for user {UserId}.", userId);
            throw;
        }
    }

    public async Task<bool> ResetLockoutAsync(Guid userId, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var updated = await context.Users
                        .Where(u => u.Id == userId)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(u => u.FailedLoginAttempts, 0)
                                .SetProperty(u => u.LockedUntil, (DateTime?)null),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (updated == 0)
                    {
                        return false;
                    }

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Security,
                            Operation = "ResetLockout",
                            EntityId = userId.ToString(),
                            EntityType = nameof(User),
                            Detail = $"Lockout reset for user {userId}",
                            Timestamp = _time.GetUtcNow().UtcDateTime,
                            UserId = modifiedByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The user repository failed to reset the lockout of user {UserId}.", userId);
            throw;
        }
    }

    public async Task<PagedList<User>> QueryAsync(
        UserQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var users = context.Users.AsNoTracking().AsQueryable();

            if (query.Username is not null)
            {
                users = users.Where(u => u.Username == query.Username);
            }

            if (query.Enabled is not null)
            {
                users = users.Where(u => u.Enabled == query.Enabled);
            }

            if (query.Role is not null)
            {
                users = users.Where(u => u.Role == query.Role);
            }

            if (query.ProviderType is not null)
            {
                users = users.Where(u => u.ProviderType == query.ProviderType);
            }

            if (query.ExternalId is not null)
            {
                users = users.Where(u => u.ExternalId == query.ExternalId);
            }

            var totalCount = await users.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortProperty = typeof(User).GetProperties()
                .Select(property => property.Name)
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                users = sort.Direction == SortDirection.Ascending
                    ? users.OrderBy(u => EF.Property<User>(u, sortProperty)).ThenBy(u => u.Id)
                    : users.OrderByDescending(u => EF.Property<User>(u, sortProperty)).ThenBy(u => u.Id);
            }
            else
            {
                users = users.OrderBy(u => u.Username).ThenBy(u => u.Id);
            }

            users = users.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            var items = await users.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = items,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalItems = totalCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The user repository failed to query users.");
            throw;
        }
    }
}
