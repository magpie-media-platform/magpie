using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Models.Extensions;
using Magpie.Models.History;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing extensions.
/// </summary>
public sealed class ExtensionRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<ExtensionRepository> logger) : IExtensionRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Extension>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.Extensions
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Extension>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting extensions ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<Extension>>(
                "Error retrieving extensions",
                ErrorCodes.DbExtensionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Extension?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from e in context.Extensions.AsNoTracking()
                where e.Id == id
                select e)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting extension {ExtensionId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<Extension?>(
                "Error retrieving extension",
                ErrorCodes.DbExtensionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Extension?>> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from e in context.Extensions.AsNoTracking()
                where e.Name == name
                select e)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting extension {ExtensionName} ({ExErrorCode})",
                name,
                errorCode ?? "");
            return Result.Fail<Extension?>(
                "Error retrieving extension",
                ErrorCodes.DbExtensionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Extension?>> GetByAssemblyNameAsync(
        string assemblyName,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from e in context.Extensions.AsNoTracking()
                where e.AssemblyName == assemblyName
                select e)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting extension by assembly name {AssemblyName} ({ExErrorCode})",
                assemblyName,
                errorCode ?? "");
            return Result.Fail<Extension?>(
                "Error retrieving extension",
                ErrorCodes.DbExtensionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Extension>>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from e in context.Extensions.AsNoTracking()
                where e.Enabled
                select e)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Extension>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting enabled extensions ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<Extension>>(
                "Error retrieving extensions",
                ErrorCodes.DbExtensionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Extension>>> GetManagedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from e in context.Extensions.AsNoTracking()
                where e.Managed
                select e)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Extension>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting managed extensions ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<Extension>>(
                "Error retrieving extensions",
                ErrorCodes.DbExtensionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Extension>> CreateAsync(
        Extension model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = model.CreateEntity(createdByUserId);

            context.Extensions.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(Extension),
                description: $"Created extension {model.Name}",
                userId: createdByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(entity.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error creating extension {ExtensionName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<Extension>(
                "Error creating extension",
                ErrorCodes.DbExtensionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        Extension model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.Extensions
                .Where(e => e.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Version, model.Version)
                        .SetProperty(e => e.Name, model.Name)
                        .SetProperty(e => e.TargetABI, model.TargetABI)
                        .SetProperty(e => e.FilePath, model.FilePath)
                        .SetProperty(e => e.AssemblyName, model.AssemblyName)
                        .SetProperty(e => e.Enabled, model.Enabled)
                        .SetProperty(e => e.DisabledReason, model.DisabledReason)
                        .SetProperty(e => e.LoadOrder, model.LoadOrder)
                        .SetProperty(e => e.Managed, model.Managed)
                        .SetProperty(e => e.NuGetId, model.NuGetId)
                        .SetProperty(e => e.PackageRepository, model.PackageRepository)
                        .SetProperty(e => e.AutoUpgrade, model.AutoUpgrade)
                        .SetProperty(e => e.LastChecked, model.LastChecked)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Update",
                    entityId: model.Id,
                    entityType: nameof(Extension),
                    description: $"Updated extension {model.Name}",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error updating extension {ExtensionId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating extension",
                ErrorCodes.DbExtensionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        ExtensionPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.Extensions
                .FirstOrDefaultAsync(e => e.Id == patch.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            if (patch.Version is not null)
            {
                entity.Version = patch.Version.Value!;
            }

            if (patch.Enabled is not null)
            {
                entity.Enabled = patch.Enabled.Value;
            }

            if (patch.DisabledReason is not null)
            {
                entity.DisabledReason = patch.DisabledReason.Value;
            }

            if (patch.LoadOrder is not null)
            {
                entity.LoadOrder = patch.LoadOrder.Value;
            }

            if (patch.AutoUpgrade is not null)
            {
                entity.AutoUpgrade = patch.AutoUpgrade.Value;
            }

            entity.ModifiedAt = DateTimeOffset.UtcNow;
            entity.ModifiedByUserId = modifiedByUserId;

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (updated > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Patch",
                    entityId: patch.Id,
                    entityType: nameof(Extension),
                    description: $"Patched extension {entity.Name}",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error patching extension {ExtensionId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error patching extension",
                ErrorCodes.DbExtensionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> DeleteAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.Extensions
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            var deleted = await context.Extensions
                .Where(e => e.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(Extension),
                    description: $"Deleted extension {entity.Name}",
                    userId: deletedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(deleted > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error deleting extension {ExtensionId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error deleting extension",
                ErrorCodes.DbExtensionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateLastCheckedAsync(
        Guid id,
        DateTimeOffset lastChecked,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.Extensions
                .Where(e => e.Id == id)
                .ExecuteUpdateAsync(
                    setter => setter.SetProperty(e => e.LastChecked, lastChecked),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error updating extension last checked {ExtensionId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating extension last checked",
                ErrorCodes.DbExtensionError,
                errorCode);
        }
    }
}
