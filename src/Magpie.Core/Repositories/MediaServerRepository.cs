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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

/// <summary>
/// Repository for managing media server configurations.
/// </summary>
public sealed class MediaServerRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<MediaServerRepository> logger) : IMediaServerRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<MediaServer>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.MediaServers
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing media servers");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<MediaServer?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.MediaServers
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting media server {MediaServerId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<MediaServer> CreateAsync(
        MediaServer model,
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

                context.MediaServers.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.Integrations,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(MediaServer),
                    Detail = $"Created media server {model.Name}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating media server {MediaServerName}", model.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(MediaServer model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var updated = await context.MediaServers
                    .Where(s => s.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(m => m.Name, model.Name)
                            .SetProperty(m => m.ExtensionId, model.ExtensionId)
                            .SetProperty(m => m.ProviderType, model.ProviderType)
                            .SetProperty(m => m.Enabled, model.Enabled)
                            .SetProperty(m => m.Status, model.Status)
                            .SetProperty(m => m.LastChecked, model.LastChecked)
                            .SetProperty(m => m.Settings, model.Settings)
                            .SetProperty(m => m.ModifiedAt, now)
                            .SetProperty(m => m.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(MediaServer),
                        Detail = $"Updated media server {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating media server {MediaServerId}", model.Id);
            throw;
        }
    }

    /// <inheritdoc />
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
                var model = await context.MediaServers
                    .AsNoTracking()
                    .Where(s => s.Id == id)
                    .Select(s => new { s.Name })
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
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(MediaServer),
                        Detail = $"Deleted media server {model.Name}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting media server {MediaServerId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> PatchAsync(
        MediaServerPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var model = await context.MediaServers
                    .FirstOrDefaultAsync(s => s.Id == patch.Id, cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var now = DateTime.UtcNow;
                if (patch.Name is not null)
                {
                    model.Name = patch.Name.Value;
                }
                if (patch.Enabled is not null)
                {
                    model.Enabled = patch.Enabled.Value;
                }
                if (patch.Settings is not null)
                {
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
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Patch",
                        EntityId = patch.Id.ToString(),
                        EntityType = nameof(MediaServer),
                        Detail = $"Patched media server {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);

                    foreach (var tagName in tagsAdded)
                    {
                        await auditRecordRepository.CreateAsync(new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "AddTag",
                            EntityId = patch.Id.ToString(),
                            EntityType = nameof(MediaServer),
                            Detail = $"Added tag '{tagName}' to media server {model.Name}",
                            Timestamp = now,
                            UserId = modifiedByUserId
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    foreach (var tagName in tagsRemoved)
                    {
                        await auditRecordRepository.CreateAsync(new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "RemoveTag",
                            EntityId = patch.Id.ToString(),
                            EntityType = nameof(MediaServer),
                            Detail = $"Removed tag '{tagName}' from media server {model.Name}",
                            Timestamp = now,
                            UserId = modifiedByUserId
                        }, cancellationToken).ConfigureAwait(false);
                    }
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error patching media server {MediaServerId}", patch.Id);
            throw;
        }
    }
}
