using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Models.History;
using Magpie.Models.Result;
using Magpie.Models.RootFolders;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing root folder configurations.
/// </summary>
public sealed class RootFolderRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<RootFolderRepository> logger) : IRootFolderRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RootFolder>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rootFolders = await context.RootFolders
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = rootFolders.Select(rf => rf.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<RootFolder>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting root folders ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<RootFolder>>(
                "Error retrieving root folders",
                ErrorCodes.DbRootFolderError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<RootFolder?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var rootFolder = await (
                from rf in context.RootFolders.AsNoTracking()
                where rf.Id == id
                select rf)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(rootFolder?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting root folder {RootFolderId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<RootFolder?>("Error retrieving root folder", ErrorCodes.DbRootFolderError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RootFolder>>> GetByContentTypeIdAsync(
        Guid contentTypeId,
        CancellationToken cancellationToken)
    {
        try
        {
            var rootFolders = await (
                from rf in context.RootFolders.AsNoTracking()
                where rf.ContentTypeId == contentTypeId
                select rf)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = rootFolders.Select(rf => rf.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<RootFolder>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting root folders for content type {ContentTypeId} ({ExErrorCode})",
                contentTypeId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<RootFolder>>(
                "Error retrieving root folders",
                ErrorCodes.DbRootFolderError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<RootFolder?>> GetByPathAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var rootFolder = await (
                from rf in context.RootFolders.AsNoTracking()
                where rf.Path == path
                select rf)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(rootFolder?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting root folder by path {Path} ({ExErrorCode})",
                path,
                errorCode ?? "");
            return Result.Fail<RootFolder?>("Error retrieving root folder", ErrorCodes.DbRootFolderError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<RootFolder>> CreateAsync(
        RootFolder model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = model.CreateEntity(createdByUserId);

            context.RootFolders.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(RootFolder),
                description: $"Created root folder {model.Name}",
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
                "Error creating root folder {RootFolderName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<RootFolder>("Error creating root folder", ErrorCodes.DbRootFolderError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        RootFolder model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.RootFolders
                .Where(rf => rf.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Name, model.Name)
                        .SetProperty(e => e.Path, model.Path)
                        .SetProperty(e => e.ContentTypeId, model.ContentTypeId)
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
                    entityType: nameof(RootFolder),
                    description: $"Updated root folder {model.Name}",
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
                "Error updating root folder {RootFolderId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>("Error updating root folder", ErrorCodes.DbRootFolderError, errorCode);
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

            var rootFolder = await context.RootFolders
                .FirstOrDefaultAsync(rf => rf.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (rootFolder is null)
            {
                return Result.Success(false);
            }

            var deleted = await context.RootFolders
                .Where(rf => rf.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(RootFolder),
                    description: $"Deleted root folder {rootFolder.Name}",
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
                "Error deleting root folder {RootFolderId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>("Error deleting root folder", ErrorCodes.DbRootFolderError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        RootFolderPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var rootFolder = await context.RootFolders
                .FirstOrDefaultAsync(rf => rf.Id == patch.Id, cancellationToken)
                .ConfigureAwait(false);

            if (rootFolder is null)
            {
                return Result.Success(false);
            }

            if (patch.Name is not null)
            {
                rootFolder.Name = patch.Name.Value!;
            }

            if (patch.Path is not null)
            {
                rootFolder.Path = patch.Path.Value!;
            }

            if (patch.ContentTypeId is not null)
            {
                rootFolder.ContentTypeId = patch.ContentTypeId.Value;
            }

            rootFolder.ModifiedAt = DateTimeOffset.UtcNow;
            rootFolder.ModifiedByUserId = modifiedByUserId;

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (updated > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Patch",
                    entityId: patch.Id,
                    entityType: nameof(RootFolder),
                    description: $"Patched root folder {rootFolder.Name}",
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
                "Error patching root folder {RootFolderId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>("Error patching root folder", ErrorCodes.DbRootFolderError, errorCode);
        }
    }
}
