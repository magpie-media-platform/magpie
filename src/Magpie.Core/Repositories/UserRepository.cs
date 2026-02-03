using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class UserRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<UserRepository> logger) : IUserRepository
{
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
            logger.LogError(ex, "Error listing users");
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
            logger.LogError(ex, "Error getting user {UserId}", id);
            throw;
        }
    }

    public async Task<User?> GetUserAsync(string username, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == username, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting user by username {Username}", username);
            throw;
        }
    }

    public async Task<User?> GetUserExternalAsync(
        Guid authProviderId,
        string externalId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    u => u.ExtensionId == authProviderId && u.ExternalId == externalId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error getting user by external id {ExternalId} for auth provider {AuthProviderId}",
                externalId,
                authProviderId);
            throw;
        }
    }

    public async Task<User> CreateAsync(
        User model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                model.CreatedAt = now;
                model.CreatedByUserId = createdByUserId;

                context.Users.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.Security,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(User),
                    Detail = $"Created user {model.Username}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating user {Username}", model.Username);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        User model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
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
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Security,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(User),
                        Detail = $"Updated user {model.Username}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating user {UserId}", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var model = await context.Users
                    .AsNoTracking()
                    .Where(u => u.Id == id)
                    .Select(u => new { u.Username })
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
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Security,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(User),
                        Detail = $"Deleted user {model.Username}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting user {UserId}", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        UserPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
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
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Security,
                        Operation = "Patch",
                        EntityId = patch.Id.ToString(),
                        EntityType = nameof(User),
                        Detail = $"Patched user {model.Username}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error patching user {UserId}", patch.Id);
            throw;
        }
    }

    public async Task<User?> GetUserAsync(
        string providerType,
        string externalId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    u => u.ProviderType == providerType && u.ExternalId == externalId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error getting user by external id {ExternalId} for provider type {ProviderType}",
                externalId,
                providerType);
            throw;
        }
    }

    public async Task<PagedList<User>> QueryAsync(
        UserQuery query,
        PagingParameter? paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var users = context.Users.AsNoTracking().AsQueryable();

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

            var totalCount = await users.CountAsync(cancellationToken).ConfigureAwait(false);

            if (sort is not null)
            {
                users = sort.Direction == SortDirection.Ascending
                    ? users.OrderBy(u => EF.Property<User>(u, sort.PropertyName))
                    : users.OrderByDescending(u => EF.Property<User>(u, sort.PropertyName));
            }
            else
            {
                users = users.OrderBy(u => u.Username);
            }

            if (paging is not null)
            {
                users = users.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);
            }

            var items = await users.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = items,
                PageNumber = paging?.PageNumber,
                PageSize = paging?.PageSize,
                TotalItems = totalCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error querying users");
            throw;
        }
    }
}
