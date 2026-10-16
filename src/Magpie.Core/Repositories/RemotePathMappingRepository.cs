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
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class RemotePathMappingRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<RemotePathMappingRepository> logger,
    TimeProvider timeProvider) : IRemotePathMappingRepository
{
    private readonly TimeProvider _time = timeProvider;

    private static List<string> ChangedFields(RemotePathMappingPatch patch)
    {
        var fields = new List<string>();
        if (patch.Host is not null)
        {
            fields.Add(nameof(RemotePathMapping.Host));
        }

        if (patch.RemotePath is not null)
        {
            fields.Add(nameof(RemotePathMapping.RemotePath));
        }

        if (patch.LocalPath is not null)
        {
            fields.Add(nameof(RemotePathMapping.LocalPath));
        }

        return fields;
    }

    private static List<string> ChangedFields(RemotePathMapping current, RemotePathMapping model)
    {
        var fields = new List<string>();
        if (!string.Equals(current.Host, model.Host, StringComparison.Ordinal))
        {
            fields.Add(nameof(RemotePathMapping.Host));
        }

        if (!string.Equals(current.RemotePath, model.RemotePath, StringComparison.Ordinal))
        {
            fields.Add(nameof(RemotePathMapping.RemotePath));
        }

        if (!string.Equals(current.LocalPath, model.LocalPath, StringComparison.Ordinal))
        {
            fields.Add(nameof(RemotePathMapping.LocalPath));
        }

        return fields;
    }

    public async Task<IEnumerable<RemotePathMapping>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.RemotePathMappings
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The remote path mapping repository failed to list remote path mappings.");
            throw;
        }
    }

    public async Task<RemotePathMapping?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.RemotePathMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The remote path mapping repository failed to read remote path mapping {RemotePathMappingId}.",
                id);
            throw;
        }
    }

    public async Task<RemotePathMapping> CreateAsync(
        RemotePathMapping model,
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

                    context.RemotePathMappings.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.System,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(RemotePathMapping),
                            Detail
                                = $"Created remote path mapping {model.Host}: {model.RemotePath} -> {model.LocalPath}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    DownloadEventBuilder.BuildRemotePathMappingCreatedEvent(created.Id),
                    token),
                cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The remote path mapping repository failed to create a remote path mapping for host {Host}.",
                model.Host);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        RemotePathMapping model,
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
                    var current = await context.RemotePathMappings
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Id == model.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (current is null)
                    {
                        return false;
                    }

                    changedFields = ChangedFields(current, model);
                    var updated = await context.RemotePathMappings
                        .Where(r => r.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(r => r.Host, model.Host)
                                .SetProperty(r => r.RemotePath, model.RemotePath)
                                .SetProperty(r => r.LocalPath, model.LocalPath)
                                .SetProperty(r => r.ModifiedAt, now)
                                .SetProperty(r => r.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(RemotePathMapping),
                                Detail =
                                    $"Updated remote path mapping {model.Host}: {model.RemotePath} -> {model.LocalPath}",
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
                        DownloadEventBuilder.BuildRemotePathMappingUpdatedEvent(model.Id, changedFields),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return applied;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The remote path mapping repository failed to update remote path mapping {RemotePathMappingId}.",
                model.Id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        RemotePathMappingPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var model = await context.RemotePathMappings
                        .FirstOrDefaultAsync(r => r.Id == patch.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (model is null)
                    {
                        return false;
                    }

                    var now = _time.GetUtcNow().UtcDateTime;
                    if (patch.Host is not null)
                    {
                        model.Host = patch.Host.Value;
                    }

                    if (patch.RemotePath is not null)
                    {
                        model.RemotePath = patch.RemotePath.Value;
                    }

                    if (patch.LocalPath is not null)
                    {
                        model.LocalPath = patch.LocalPath.Value;
                    }

                    model.ModifiedAt = now;
                    model.ModifiedByUserId = modifiedByUserId;

                    var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Patch",
                                EntityId = patch.Id.ToString(),
                                EntityType = nameof(RemotePathMapping),
                                Detail =
                                    $"Updated remote path mapping {model.Host}: {model.RemotePath} -> {model.LocalPath}",
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
                        DownloadEventBuilder.BuildRemotePathMappingUpdatedEvent(patch.Id, ChangedFields(patch)),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The remote path mapping repository failed to patch remote path mapping {RemotePathMappingId}.",
                patch.Id);
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
                    var model = await (
                        from r in context.RemotePathMappings.AsNoTracking()
                        where r.Id == id
                        select new { r.Host, r.RemotePath })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var deleted = await context.RemotePathMappings
                        .Where(r => r.Id == id)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (deleted > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Delete",
                                EntityId = id.ToString(),
                                EntityType = nameof(RemotePathMapping),
                                Detail = $"Deleted remote path mapping {model.Host}: {model.RemotePath}",
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
                    token => eventBus.PublishAsync(DownloadEventBuilder.BuildRemotePathMappingDeletedEvent(id), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The remote path mapping repository failed to delete remote path mapping {RemotePathMappingId}.",
                id);
            throw;
        }
    }
}
