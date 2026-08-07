using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core.Events;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class DownloadClientRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<DownloadClientRepository> logger,
    IDataProtectionService protector,
    TimeProvider? timeProvider = null) : IDownloadClientRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private static IReadOnlyCollection<string> ChangedFields(DownloadClientPatch patch)
    {
        var fields = new List<string>();
        if (patch.Name is not null)
        {
            fields.Add("name");
        }

        if (patch.Enabled is not null)
        {
            fields.Add("enabled");
        }

        if (patch.Priority is not null)
        {
            fields.Add("priority");
        }

        if (patch.CleanupDeleteFiles is not null)
        {
            fields.Add("cleanupDeleteFiles");
        }

        if (patch.KeepFailedOnClient is not null)
        {
            fields.Add("keepFailedOnClient");
        }

        if (patch.Settings is not null)
        {
            fields.Add("settings");
        }

        if (patch.Status is not null)
        {
            fields.Add("status");
        }

        if (patch.LastChecked is not null)
        {
            fields.Add("lastChecked");
        }

        if (patch.LastError is not null)
        {
            fields.Add("lastError");
        }

        if (patch.ConsecutiveFailures is not null)
        {
            fields.Add("consecutiveFailures");
        }

        if (patch.Tags is not null)
        {
            fields.Add("tags");
        }

        return fields;
    }

    public async Task<IEnumerable<DownloadClient>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var clients = await context.DownloadClients
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var client in clients)
            {
                client.Settings = protector.Unprotect(client.Settings);
            }

            return clients;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download client repository failed to list the download clients.");
            throw;
        }
    }

    public async Task<DownloadClient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await context.DownloadClients
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (client is not null)
            {
                client.Settings = protector.Unprotect(client.Settings);
            }

            return client;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download client repository failed to read download client " +
                "{DownloadClientId}.", id);
            throw;
        }
    }

    public async Task<DownloadClient> CreateAsync(
        DownloadClient model,
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

                    var plaintext = model.Settings;
                    model.Settings = protector.Protect(plaintext);

                    context.DownloadClients.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    context.Entry(model).State = EntityState.Detached;
                    model.Settings = plaintext;

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(DownloadClient),
                            Detail = $"Created download client {model.Name}",
                            Timestamp = _time.GetUtcNow().UtcDateTime,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(DownloadEventBuilder.BuildDownloadClientCreatedEvent(created.Id), token),
                cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download client repository failed to create download client " +
                "{DownloadClientName}.", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        DownloadClient model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var encryptedSettings = protector.Protect(model.Settings);

                    var updated = await context.DownloadClients
                        .Where(c => c.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(c => c.Name, model.Name)
                                .SetProperty(c => c.ProviderType, model.ProviderType)
                                .SetProperty(c => c.Enabled, model.Enabled)
                                .SetProperty(c => c.Priority, model.Priority)
                                .SetProperty(c => c.Status, model.Status)
                                .SetProperty(c => c.LastChecked, model.LastChecked)
                                .SetProperty(c => c.CleanupDeleteFiles, model.CleanupDeleteFiles)
                                .SetProperty(c => c.KeepFailedOnClient, model.KeepFailedOnClient)
                                .SetProperty(c => c.Settings, encryptedSettings)
                                .SetProperty(c => c.ModifiedAt, now)
                                .SetProperty(c => c.ModifiedByUserId, modifiedByUserId),
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
                                EntityType = nameof(DownloadClient),
                                Detail = $"Updated download client {model.Name}",
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
            logger.LogError(ex, "The download client repository failed to update download client " +
                "{DownloadClientId}.", model.Id);
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
                    var model = await (
                        from c in context.DownloadClients.AsNoTracking()
                        where c.Id == id
                        select new { c.Name })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var deleted = await context.DownloadClients
                        .Where(c => c.Id == id)
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
                                EntityType = nameof(DownloadClient),
                                Detail = $"Deleted download client {model.Name}",
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
                    token => eventBus.PublishAsync(DownloadEventBuilder.BuildDownloadClientDeletedEvent(id), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download client repository failed to delete download client " +
                "{DownloadClientId}.", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        DownloadClientPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var model = await context.DownloadClients
                        .FirstOrDefaultAsync(c => c.Id == patch.Id, cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var now = _time.GetUtcNow().UtcDateTime;
                    if (patch.Name is not null)
                    {
                        model.Name = patch.Name.Value;
                    }

                    if (patch.Enabled is not null)
                    {
                        model.Enabled = patch.Enabled.Value;
                    }

                    if (patch.Priority is not null)
                    {
                        model.Priority = patch.Priority.Value;
                    }

                    if (patch.CleanupDeleteFiles is not null)
                    {
                        model.CleanupDeleteFiles = patch.CleanupDeleteFiles.Value;
                    }

                    if (patch.KeepFailedOnClient is not null)
                    {
                        model.KeepFailedOnClient = patch.KeepFailedOnClient.Value;
                    }

                    if (patch.Status is not null)
                    {
                        model.Status = patch.Status.Value;
                    }

                    if (patch.LastChecked is not null)
                    {
                        model.LastChecked = patch.LastChecked.Value;
                    }

                    if (patch.LastError is not null)
                    {
                        var error = patch.LastError.Value;
                        model.LastError = error is { Length: > 1024 } ? error[..1024] : error;
                    }

                    if (patch.ConsecutiveFailures is not null)
                    {
                        model.ConsecutiveFailures = patch.ConsecutiveFailures.Value;
                    }

                    if (patch.Settings is not null)
                    {
                        var settings = protector.Unprotect(model.Settings);
                        if (patch.Settings.Add is not null)
                        {
                            foreach (var (key, value) in patch.Settings.Add)
                            {
                                settings[key] = value ?? string.Empty;
                            }
                        }

                        if (patch.Settings.Update is not null)
                        {
                            foreach (var (key, value) in patch.Settings.Update)
                            {
                                if (settings.ContainsKey(key))
                                {
                                    settings[key] = value ?? string.Empty;
                                }
                            }
                        }

                        if (patch.Settings.Remove is not null)
                        {
                            foreach (var key in patch.Settings.Remove)
                            {
                                settings.Remove(key);
                            }
                        }

                        model.Settings = protector.Protect(settings);
                        context.Entry(model).Property(c => c.Settings).IsModified = true;
                    }

                    var tagsAdded = new List<string>();
                    var tagsRemoved = new List<string>();

                    if (patch.Tags is not null)
                    {
                        if (patch.Tags.Add is not null)
                        {
                            foreach (var tagName in patch.Tags.Add)
                            {
                                var tag = await context.Tags
                                    .Include(t => t.DownloadClients)
                                    .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                    .ConfigureAwait(false);

                                if (tag is null)
                                {
                                    tag = new() { Name = tagName };
                                    context.Tags.Add(tag);
                                }

                                if (!tag.DownloadClients.Contains(model))
                                {
                                    tag.DownloadClients.Add(model);
                                    tagsAdded.Add(tagName);
                                }
                            }
                        }

                        if (patch.Tags.Remove is not null)
                        {
                            foreach (var tagName in patch.Tags.Remove)
                            {
                                var tag = await context.Tags
                                    .Include(t => t.DownloadClients)
                                    .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                    .ConfigureAwait(false);

                                if (tag is not null && tag.DownloadClients.Remove(model))
                                {
                                    tagsRemoved.Add(tagName);
                                }
                            }
                        }
                    }

                    model.ModifiedAt = now;
                    model.ModifiedByUserId = modifiedByUserId;

                    var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Integrations,
                                Operation = "Patch",
                                EntityId = patch.Id.ToString(),
                                EntityType = nameof(DownloadClient),
                                Detail = $"Updated download client {model.Name}.",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);

                        foreach (var tagName in tagsAdded)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.Integrations,
                                    Operation = "AddTag",
                                    EntityId = patch.Id.ToString(),
                                    EntityType = nameof(DownloadClient),
                                    Detail = $"Added tag {tagName} to download client {model.Name}",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                },
                                cancellationToken).ConfigureAwait(false);
                        }

                        foreach (var tagName in tagsRemoved)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.Integrations,
                                    Operation = "RemoveTag",
                                    EntityId = patch.Id.ToString(),
                                    EntityType = nameof(DownloadClient),
                                    Detail = $"Removed tag {tagName} from download client {model.Name}",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                },
                                cancellationToken).ConfigureAwait(false);
                        }
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (patched)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        DownloadEventBuilder.BuildDownloadClientUpdatedEvent(patch.Id, ChangedFields(patch)),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download client repository failed to patch download client " +
                "{DownloadClientId}.", patch.Id);
            throw;
        }
    }
}
