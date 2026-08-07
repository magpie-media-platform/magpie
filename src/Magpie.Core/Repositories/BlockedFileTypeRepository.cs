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
using Magpie.Models.Data;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class BlockedFileTypeRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<BlockedFileTypeRepository> logger,
    TimeProvider? timeProvider = null) : IBlockedFileTypeRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<IEnumerable<BlockedFileType>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.BlockedFileTypes
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The blocked file type repository failed to list the blocked file types.");
            throw;
        }
    }

    public async Task<BlockedFileType?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.BlockedFileTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The blocked file type repository failed to read blocked file type " +
                "{BlockedFileTypeId}.", id);
            throw;
        }
    }

    public async Task<BlockedFileType> CreateAsync(
        BlockedFileType model,
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

                    context.BlockedFileTypes.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.System,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(BlockedFileType),
                            Detail = $"Created blocked file type {model.Pattern}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    ConfigurationEventBuilder.BuildBlockedFileTypeCreatedEvent(created.Id),
                    token),
                cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The blocked file type repository failed to create blocked file type " +
                "{Pattern}.", model.Pattern);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        BlockedFileType model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var updated = await context.BlockedFileTypes
                        .Where(e => e.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(e => e.Pattern, model.Pattern)
                                .SetProperty(e => e.ModifiedAt, now)
                                .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
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
                                EntityType = nameof(BlockedFileType),
                                Detail = $"Updated blocked file type {model.Pattern}",
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
            logger.LogError(ex, "The blocked file type repository failed to update blocked file type " +
                "{BlockedFileTypeId}.", model.Id);
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
                    var existing = await (
                        from e in context.BlockedFileTypes.AsNoTracking()
                        where e.Id == id
                        select new { e.Pattern })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (existing is null)
                    {
                        return false;
                    }

                    var deleted = await context.BlockedFileTypes
                        .Where(e => e.Id == id)
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
                                EntityType = nameof(BlockedFileType),
                                Detail = $"Deleted blocked file type {existing.Pattern}",
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
                        ConfigurationEventBuilder.BuildBlockedFileTypeDeletedEvent(id),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The blocked file type repository failed to delete blocked file type " +
                "{BlockedFileTypeId}.", id);
            throw;
        }
    }
}
