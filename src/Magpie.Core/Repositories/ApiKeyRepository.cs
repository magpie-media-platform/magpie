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
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class ApiKeyRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<ApiKeyRepository> logger,
    TimeProvider? timeProvider = null) : IApiKeyRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private static List<string> ChangedFields(ApiKey current, ApiKey model)
    {
        var fields = new List<string>();
        if (!string.Equals(current.Name, model.Name, StringComparison.Ordinal))
        {
            fields.Add("name");
        }

        if (current.Enabled != model.Enabled)
        {
            fields.Add("enabled");
        }

        if (current.ExpiresAt != model.ExpiresAt)
        {
            fields.Add("expiresAt");
        }

        if (current.LastUsedAt != model.LastUsedAt)
        {
            fields.Add("lastUsedAt");
        }

        return fields;
    }

    private static List<string> ChangedFields(ApiKeyPatch patch)
    {
        var fields = new List<string>();
        if (patch.Enabled is not null)
        {
            fields.Add("enabled");
        }

        return fields;
    }

    public async Task<IEnumerable<ApiKey>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.ApiKeys
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The API key repository failed to list the API keys.");
            throw;
        }
    }

    public async Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.ApiKeys
                .AsNoTracking()
                .FirstOrDefaultAsync(k => k.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The API key repository failed to read API key {ApiKeyId}.", id);
            throw;
        }
    }

    public async Task<ApiKey> CreateAsync(
        ApiKey model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    model.CreatedAt = _time.GetUtcNow().UtcDateTime;
                    model.CreatedByUserId = createdByUserId;

                    context.ApiKeys.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Security,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            ParentEntityId = model.UserId.ToString(),
                            EntityType = nameof(ApiKey),
                            Detail = $"Created API key {model.Name}",
                            Timestamp = _time.GetUtcNow().UtcDateTime,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    SecurityEventBuilder.BuildApiKeyCreatedEvent(created.Id, created.UserId),
                    token),
                cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The API key repository failed to create API key {ApiKeyName}.", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(ApiKey model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var changedFields = new List<string>();
            var ownerUserId = Guid.Empty;
            var updated = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var current = await context.ApiKeys
                        .AsNoTracking()
                        .FirstOrDefaultAsync(k => k.Id == model.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (current is null)
                    {
                        return false;
                    }

                    ownerUserId = current.UserId;
                    changedFields = ChangedFields(current, model);

                    var affected = await context.ApiKeys
                        .Where(k => k.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(k => k.Name, model.Name)
                                .SetProperty(k => k.Enabled, model.Enabled)
                                .SetProperty(k => k.ExpiresAt, model.ExpiresAt)
                                .SetProperty(k => k.LastUsedAt, model.LastUsedAt)
                                .SetProperty(k => k.ModifiedAt, now)
                                .SetProperty(k => k.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (affected > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Security,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                ParentEntityId = current.UserId.ToString(),
                                EntityType = nameof(ApiKey),
                                Detail = $"Updated API key {model.Name}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return affected > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (updated)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        SecurityEventBuilder.BuildApiKeyUpdatedEvent(model.Id, ownerUserId, changedFields), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return updated;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The API key repository failed to update API key {ApiKeyId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(ApiKeyPatch patch, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var ownerUserId = Guid.Empty;
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var model = await context.ApiKeys
                        .FirstOrDefaultAsync(k => k.Id == patch.Id, cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    ownerUserId = model.UserId;

                    if (patch.Enabled is not null)
                    {
                        model.Enabled = patch.Enabled.Value;
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
                                ParentEntityId = model.UserId.ToString(),
                                EntityType = nameof(ApiKey),
                                Detail = $"Patched API key {model.Name}",
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
                        SecurityEventBuilder.BuildApiKeyUpdatedEvent(
                            patch.Id,
                            ownerUserId,
                            ChangedFields(patch)), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The API key repository failed to patch API key {ApiKeyId}.", patch.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var ownerUserId = Guid.Empty;
            var removed = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var model = await (
                        from k in context.ApiKeys.AsNoTracking()
                        where k.Id == id
                        select new { k.UserId, k.Name })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    ownerUserId = model.UserId;

                    var deleted = await context.ApiKeys
                        .Where(k => k.Id == id)
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
                                ParentEntityId = model.UserId.ToString(),
                                EntityType = nameof(ApiKey),
                                Detail = $"Deleted API key {model.Name}",
                                Timestamp = _time.GetUtcNow().UtcDateTime,
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
                    token => eventBus.PublishAsync(
                        SecurityEventBuilder.BuildApiKeyDeletedEvent(id, ownerUserId),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The API key repository failed to delete API key {ApiKeyId}.", id);
            throw;
        }
    }

    public async Task<PagedList<ApiKey>> QueryAsync(
        ApiKeyQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var keys = context.ApiKeys.AsNoTracking().AsQueryable();

            if (query.KeyHash is { } keyHash)
            {
                keys = keys.Where(key => key.KeyHash == keyHash);
            }

            if (query.UserId is not null)
            {
                keys = keys.Where(k => k.UserId == query.UserId);
            }

            if (query.Enabled is not null)
            {
                keys = keys.Where(k => k.Enabled == query.Enabled);
            }

            if (query.Expired is not null)
            {
                var now = _time.GetUtcNow().UtcDateTime;
                keys = query.Expired.Value
                    ? keys.Where(k => k.ExpiresAt != null && k.ExpiresAt <= now)
                    : keys.Where(k => k.ExpiresAt == null || k.ExpiresAt > now);
            }

            var totalCount = await keys.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortProperty = typeof(ApiKey).GetProperties()
                .Select(property => property.Name)
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                keys = sort.Direction == SortDirection.Ascending
                    ? keys.OrderBy(k => EF.Property<ApiKey>(k, sortProperty)).ThenBy(k => k.Id)
                    : keys.OrderByDescending(k => EF.Property<ApiKey>(k, sortProperty)).ThenBy(k => k.Id);
            }
            else
            {
                keys = keys.OrderBy(k => k.Name).ThenBy(k => k.Id);
            }

            keys = keys.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            var items = await keys.ToListAsync(cancellationToken).ConfigureAwait(false);

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
            logger.LogError(ex, "The API key repository failed to query the API keys.");
            throw;
        }
    }
}
