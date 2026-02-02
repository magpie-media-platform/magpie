using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

/// <summary>
/// Repository for managing root folder configurations.
/// </summary>
public sealed class RootFolderRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<RootFolderRepository> logger) : IRootFolderRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<RootFolder>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.RootFolders
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing root folders");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<RootFolder?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.RootFolders
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting root folder {RootFolderId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<RootFolder> CreateAsync(
        RootFolder model,
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

                context.RootFolders.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.System,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(RootFolder),
                    Detail = $"Created root folder {model.Name}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating root folder {RootFolderName}", model.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(RootFolder model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var updated = await context.RootFolders
                    .Where(r => r.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(r => r.Name, model.Name)
                            .SetProperty(r => r.Path, model.Path)
                            .SetProperty(r => r.ContentTypeId, model.ContentTypeId)
                            .SetProperty(r => r.ModifiedAt, now)
                            .SetProperty(r => r.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(RootFolder),
                        Detail = $"Updated root folder {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating root folder {RootFolderId}", model.Id);
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
                var model = await context.RootFolders
                    .AsNoTracking()
                    .Where(r => r.Id == id)
                    .Select(r => new { r.Name })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var deleted = await context.RootFolders
                    .Where(r => r.Id == id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(RootFolder),
                        Detail = $"Deleted root folder {model.Name}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting root folder {RootFolderId}", id);
            throw;
        }
    }
}
