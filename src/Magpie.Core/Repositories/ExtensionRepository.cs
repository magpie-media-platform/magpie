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

internal sealed class ExtensionRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<ExtensionRepository> logger) : IExtensionRepository
{
    public async Task<IEnumerable<Extension>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.Extensions
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing extensions");
            throw;
        }
    }

    public async Task<Extension?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Extensions
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting extension {ExtensionId}", id);
            throw;
        }
    }

    public async Task<Extension> CreateAsync(
        Extension model,
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

                context.Extensions.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.System,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(Extension),
                    Detail = $"Created extension {model.Name}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating extension {ExtensionName}", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(Extension model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;

                var updated = await context.Extensions
                    .Where(e => e.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(e => e.Version, model.Version)
                            .SetProperty(e => e.Name, model.Name)
                            .SetProperty(e => e.TargetABI, model.TargetABI)
                            .SetProperty(e => e.FilePath, model.FilePath)
                            .SetProperty(e => e.AssemblyName, model.AssemblyName)
                            .SetProperty(e => e.Installed, model.Installed)
                            .SetProperty(e => e.Enabled, model.Enabled)
                            .SetProperty(e => e.DisabledReason, model.DisabledReason)
                            .SetProperty(e => e.LoadOrder, model.LoadOrder)
                            .SetProperty(e => e.Managed, model.Managed)
                            .SetProperty(e => e.NuGetId, model.NuGetId)
                            .SetProperty(e => e.PackageRepository, model.PackageRepository)
                            .SetProperty(e => e.AutoUpgrade, model.AutoUpgrade)
                            .SetProperty(e => e.LastChecked, model.LastChecked)
                            .SetProperty(e => e.ModifiedAt, now)
                            .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(Extension),
                        Detail = $"Updated extension {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating extension {ExtensionId}", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;

                var model = await context.Extensions
                    .AsNoTracking()
                    .Where(e => e.Id == id)
                    .Select(e => new { e.Name })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var deleted = await context.Extensions
                    .Where(e => e.Id == id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(Extension),
                        Detail = $"Deleted extension {model.Name}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting extension {ExtensionId}", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        ExtensionPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var model = await context.Extensions
                    .FirstOrDefaultAsync(e => e.Id == patch.Id, cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var now = DateTime.UtcNow;
                if (patch.NuGetId is not null)
                {
                    model.NuGetId = patch.NuGetId.Value;
                }
                if (patch.PackageRepository is not null)
                {
                    model.PackageRepository = patch.PackageRepository.Value;
                }
                if (patch.AutoUpgrade is not null)
                {
                    model.AutoUpgrade = patch.AutoUpgrade.Value;
                }

                model.ModifiedAt = now;
                model.ModifiedByUserId = modifiedByUserId;

                var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Patch",
                        EntityId = patch.Id.ToString(),
                        EntityType = nameof(Extension),
                        Detail = $"Patched extension {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error patching extension {ExtensionId}", patch.Id);
            throw;
        }
    }
}
