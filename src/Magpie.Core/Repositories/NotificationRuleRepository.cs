using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

internal sealed class NotificationRuleRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<NotificationRuleRepository> logger,
    TimeProvider? timeProvider = null) : INotificationRuleRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private static List<string> ChangedFields(NotificationRule current, NotificationRule model)
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

        if (current.OwnerUserId != model.OwnerUserId)
        {
            fields.Add("ownerUserId");
        }

        if (!string.Equals(current.ProviderType, model.ProviderType, StringComparison.Ordinal))
        {
            fields.Add("providerType");
        }

        if (!SettingsEqual(current.Settings, model.Settings))
        {
            fields.Add("settings");
        }

        if (!string.Equals(
            JsonSerializer.Serialize(current.EventFilter),
            JsonSerializer.Serialize(model.EventFilter),
            StringComparison.Ordinal))
        {
            fields.Add("eventFilter");
        }

        return fields;
    }

    private static bool SettingsEqual(
        IReadOnlyDictionary<string, string> current,
        IReadOnlyDictionary<string, string> model)
    {
        return current.Count == model.Count &&
            current.All(pair => model.TryGetValue(pair.Key, out var value) &&
                string.Equals(pair.Value, value, StringComparison.Ordinal));
    }

    public async Task<IEnumerable<NotificationRule>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.NotificationRules
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The notification rule repository failed to list notification rules.");
            throw;
        }
    }

    public async Task<NotificationRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.NotificationRules
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The notification rule repository failed to read notification rule {RuleId}.", id);
            throw;
        }
    }

    public async Task<PagedList<NotificationRule>> QueryAsync(
        NotificationRuleQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var rules = context.NotificationRules.AsNoTracking().AsQueryable();

            if (query.OwnerUserIds is { Count: > 0 } ownerUserIds)
            {
                rules = rules.Where(r => r.OwnerUserId != null && ownerUserIds.Contains(r.OwnerUserId!.Value));
            }

            if (query.Enabled is not null)
            {
                rules = rules.Where(r => r.Enabled == query.Enabled);
            }

            var totalCount = await rules.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortProperty = typeof(NotificationRule).GetProperties()
                .Select(property => property.Name)
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                rules = sort.Direction == SortDirection.Ascending
                    ? rules.OrderBy(r => EF.Property<NotificationRule>(r, sortProperty)).ThenBy(r => r.Id)
                    : rules.OrderByDescending(r => EF.Property<NotificationRule>(r, sortProperty)).ThenBy(r => r.Id);
            }
            else
            {
                rules = rules.OrderBy(r => r.Name).ThenBy(r => r.Id);
            }

            rules = rules.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            var items = await rules.ToListAsync(cancellationToken).ConfigureAwait(false);

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
            logger.LogError(ex, "The notification rule repository failed to query notification rules.");
            throw;
        }
    }

    public async Task<NotificationRule> CreateAsync(
        NotificationRule model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    model.CreatedAt = now;
                    model.CreatedByUserId = createdByUserId;

                    context.NotificationRules.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(NotificationRule),
                            Detail = $"Created notification rule {model.Name}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    NotificationEventBuilder.BuildNotificationRuleCreatedEvent(created.Id),
                    token),
                cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The notification rule repository failed to create notification rule " +
                "{RuleName}.", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        NotificationRule model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var changedFields = new List<string>();
            var applied = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var current = await context.NotificationRules
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Id == model.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (current is null)
                    {
                        return false;
                    }

                    changedFields = ChangedFields(current, model);
                    var updated = await context.NotificationRules
                        .Where(r => r.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(r => r.Name, model.Name)
                                .SetProperty(r => r.Enabled, model.Enabled)
                                .SetProperty(r => r.OwnerUserId, model.OwnerUserId)
                                .SetProperty(r => r.ProviderType, model.ProviderType)
                                .SetProperty(r => r.Settings, model.Settings)
                                .SetProperty(r => r.EventFilter, model.EventFilter)
                                .SetProperty(r => r.ModifiedAt, now)
                                .SetProperty(r => r.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Integrations,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(NotificationRule),
                                Detail = $"Updated notification rule {model.Name}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (applied && changedFields.Count > 0)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        NotificationEventBuilder.BuildNotificationRuleUpdatedEvent(model.Id, changedFields), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return applied;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The notification rule repository failed to update notification rule " +
                "{RuleId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            var removed = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var snapshot = await (
                        from r in context.NotificationRules.AsNoTracking()
                        where r.Id == id
                        select new { r.Name })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (snapshot is null)
                    {
                        return false;
                    }

                    var deleted = await context.NotificationRules
                        .Where(r => r.Id == id)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (deleted > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Integrations,
                                Operation = "Delete",
                                EntityId = id.ToString(),
                                EntityType = nameof(NotificationRule),
                                Detail = $"Deleted notification rule {snapshot.Name}",
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
                    token => eventBus.PublishAsync(
                        NotificationEventBuilder.BuildNotificationRuleDeletedEvent(id),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The notification rule repository failed to delete notification rule {RuleId}.", id);
            throw;
        }
    }
}
