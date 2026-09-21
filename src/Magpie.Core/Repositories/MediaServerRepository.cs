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

internal sealed class MediaServerRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<MediaServerRepository> logger,
    IDataProtectionService protector,
    TimeProvider timeProvider) : IMediaServerRepository
{
    private static readonly (string Name, Func<MediaServer, MediaServer, bool> Changed)[] UpdateFields =
    [
        ("name", (current, model) => !Equals(current.Name, model.Name)),
        ("extensionId", (current, model) => !Equals(current.ExtensionId, model.ExtensionId)),
        ("providerType", (current, model) => !Equals(current.ProviderType, model.ProviderType)),
        ("enabled", (current, model) => !Equals(current.Enabled, model.Enabled)),
        ("status", (current, model) => !Equals(current.Status, model.Status)),
        ("contentTypes", (current, model) => !current.ContentTypes.SequenceEqual(model.ContentTypes)),
        ("lastChecked", (current, model) => !Equals(current.LastChecked, model.LastChecked)),
        ("settings", (current, model) => !SettingsEqual(current.Settings, model.Settings)),
    ];

    private readonly TimeProvider _time = timeProvider;

    private static bool SettingsEqual(
        IReadOnlyDictionary<string, string> current,
        IReadOnlyDictionary<string, string> model)
    {
        return current.Count == model.Count &&
            current.All(pair => model.TryGetValue(pair.Key, out var value) &&
                string.Equals(pair.Value, value, StringComparison.Ordinal));
    }

    private static IReadOnlyCollection<string> ChangedFields(MediaServerPatch patch)
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

        if (patch.ContentTypes is not null)
        {
            fields.Add("contentTypes");
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

    private Dictionary<string, string> ReadSettings(IReadOnlyDictionary<string, string> settings)
    {
        return protector.TryGetKeyId(settings, out _) ? protector.Unprotect(settings) : new(settings);
    }

    public async Task<IEnumerable<MediaServer>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var models = await context.MediaServers
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var model in models)
            {
                model.Settings = ReadSettings(model.Settings);
            }

            return models;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The media server repository failed to list media servers.");
            throw;
        }
    }

    public async Task<MediaServer?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var model = await context.MediaServers
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                .ConfigureAwait(false);
            if (model is not null)
            {
                model.Settings = ReadSettings(model.Settings);
            }

            return model;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The media server repository failed to read media server {MediaServerId}.", id);
            throw;
        }
    }

    public async Task<MediaServer> CreateAsync(
        MediaServer model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    model.CreatedAt = now;
                    model.CreatedByUserId = createdByUserId;

                    var stored = model with { Settings = protector.Protect(model.Settings) };
                    context.MediaServers.Add(stored);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    model.Id = stored.Id;

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(MediaServer),
                            Detail = $"Created media server {model.Name}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(IntegrationEventBuilder.BuildMediaServerCreatedEvent(created.Id), token),
                cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The media server repository failed to create media server " +
                "{MediaServerName}.", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(MediaServer model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var encryptedSettings = protector.Protect(model.Settings);
                    var now = _time.GetUtcNow().UtcDateTime;
                    var current = await context.MediaServers.AsNoTracking()
                        .FirstOrDefaultAsync(entity => entity.Id == model.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (current is null)
                    {
                        return false;
                    }

                    current.Settings = ReadSettings(current.Settings);
                    var changedFields = new List<string>();
                    foreach (var (name, changed) in UpdateFields)
                    {
                        if (changed(current, model))
                        {
                            changedFields.Add(name);
                        }
                    }

                    var updated = await context.MediaServers
                        .Where(s => s.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(m => m.Name, model.Name)
                                .SetProperty(m => m.ExtensionId, model.ExtensionId)
                                .SetProperty(m => m.ProviderType, model.ProviderType)
                                .SetProperty(m => m.Enabled, model.Enabled)
                                .SetProperty(m => m.Status, model.Status)
                                .SetProperty(m => m.ContentTypes, model.ContentTypes)
                                .SetProperty(m => m.LastChecked, model.LastChecked)
                                .SetProperty(m => m.Settings, encryptedSettings)
                                .SetProperty(m => m.ModifiedAt, now)
                                .SetProperty(m => m.ModifiedByUserId, modifiedByUserId),
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
                                EntityType = nameof(MediaServer),
                                Detail = $"Updated media server {model.Name}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (updated > 0 && changedFields.Count > 0)
                    {
                        await transactionManager.OnCommittedAsync(
                            token => eventBus.PublishAsync(
                                IntegrationEventBuilder.BuildMediaServerUpdatedEvent(model.Id, changedFields), token),
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The media server repository failed to update media server {MediaServerId}.", model.Id);
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
                        from s in context.MediaServers.AsNoTracking()
                        where s.Id == id
                        select new { s.Name })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var deleted = await context.MediaServers
                        .Where(s => s.Id == id)
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
                                EntityType = nameof(MediaServer),
                                Detail = $"Deleted media server {model.Name}",
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
                    token => eventBus.PublishAsync(IntegrationEventBuilder.BuildMediaServerDeletedEvent(id), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The media server repository failed to delete media server {MediaServerId}.", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        MediaServerPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var model = await context.MediaServers
                        .FirstOrDefaultAsync(s => s.Id == patch.Id, cancellationToken)
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

                    if (patch.ContentTypes is not null)
                    {
                        model.ContentTypes = [.. patch.ContentTypes];
                        context.Entry(model).Property(s => s.ContentTypes).IsModified = true;
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
                        model.Settings = ReadSettings(model.Settings);
                        if (patch.Settings.Add is not null)
                        {
                            foreach (var (key, value) in patch.Settings.Add)
                            {
                                model.Settings[key] = value ?? string.Empty;
                            }
                        }

                        if (patch.Settings.Update is not null)
                        {
                            foreach (var (key, value) in patch.Settings.Update)
                            {
                                if (model.Settings.ContainsKey(key))
                                {
                                    model.Settings[key] = value ?? string.Empty;
                                }
                            }
                        }

                        if (patch.Settings.Remove is not null)
                        {
                            foreach (var key in patch.Settings.Remove)
                            {
                                model.Settings.Remove(key);
                            }
                        }

                        model.Settings = protector.Protect(model.Settings);
                        context.Entry(model).Property(s => s.Settings).IsModified = true;
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
                                    .Include(t => t.MediaServers)
                                    .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                    .ConfigureAwait(false);

                                if (tag is null)
                                {
                                    tag = new() { Name = tagName };
                                    context.Tags.Add(tag);
                                }

                                if (!tag.MediaServers.Contains(model))
                                {
                                    tag.MediaServers.Add(model);
                                    tagsAdded.Add(tagName);
                                }
                            }
                        }

                        if (patch.Tags.Remove is not null)
                        {
                            foreach (var tagName in patch.Tags.Remove)
                            {
                                var tag = await context.Tags
                                    .Include(t => t.MediaServers)
                                    .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                    .ConfigureAwait(false);

                                if (tag is not null && tag.MediaServers.Remove(model))
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
                                EntityType = nameof(MediaServer),
                                Detail = $"Updated media server {model.Name}.",
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
                                    EntityType = nameof(MediaServer),
                                    Detail = $"Added tag {tagName} to media server {model.Name}",
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
                                    EntityType = nameof(MediaServer),
                                    Detail = $"Removed tag {tagName} from media server {model.Name}",
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
                        IntegrationEventBuilder.BuildMediaServerUpdatedEvent(patch.Id, ChangedFields(patch)), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The media server repository failed to patch media server {MediaServerId}.", patch.Id);
            throw;
        }
    }
}
