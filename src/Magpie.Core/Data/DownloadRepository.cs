using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Models.Downloads;
using Magpie.Models.History;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing downloads.
/// </summary>
public sealed class DownloadRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<DownloadRepository> logger) : IDownloadRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Download>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.Downloads
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Download>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting downloads ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<Download>>(
                "Error retrieving downloads",
                ErrorCodes.DbDownloadError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Download?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from d in context.Downloads.AsNoTracking()
                where d.Id == id
                select d)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting download {DownloadId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<Download?>(
                "Error retrieving download",
                ErrorCodes.DbDownloadError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Download>>> GetByContentItemIdAsync(
        Guid contentItemId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from d in context.Downloads.AsNoTracking()
                where d.ContentItemId == contentItemId
                select d)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Download>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting downloads for content item {ContentItemId} ({ExErrorCode})",
                contentItemId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<Download>>(
                "Error retrieving downloads",
                ErrorCodes.DbDownloadError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Download>>> GetByDownloadClientIdAsync(
        Guid downloadClientId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from d in context.Downloads.AsNoTracking()
                where d.DownloadClientId == downloadClientId
                select d)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Download>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting downloads for download client {DownloadClientId} ({ExErrorCode})",
                downloadClientId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<Download>>(
                "Error retrieving downloads",
                ErrorCodes.DbDownloadError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Download?>> GetByExternalIdAsync(
        Guid downloadClientId,
        string externalId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from d in context.Downloads.AsNoTracking()
                where d.DownloadClientId == downloadClientId && d.ExternalId == externalId
                select d)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting download by external ID {ExternalId} for client {DownloadClientId} ({ExErrorCode})",
                externalId,
                downloadClientId,
                errorCode ?? "");
            return Result.Fail<Download?>(
                "Error retrieving download",
                ErrorCodes.DbDownloadError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Download>>> GetByStateAsync(
        DownloadState state,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from d in context.Downloads.AsNoTracking()
                where d.State == state
                select d)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Download>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting downloads by state {State} ({ExErrorCode})",
                state,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<Download>>(
                "Error retrieving downloads",
                ErrorCodes.DbDownloadError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Download>> CreateAsync(
        Download model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = model.CreateEntity(createdByUserId);

            context.Downloads.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Integrations,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(Download),
                description: $"Created download {model.Title}",
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
                "Error creating download {DownloadTitle} ({ExErrorCode})",
                model.Title,
                errorCode ?? "");
            return Result.Fail<Download>(
                "Error creating download",
                ErrorCodes.DbDownloadError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        Download model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.Downloads
                .Where(d => d.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.ContentItemId, model.ContentItemId)
                        .SetProperty(e => e.ReleaseId, model.ReleaseId)
                        .SetProperty(e => e.DownloadClientId, model.DownloadClientId)
                        .SetProperty(e => e.ExternalId, model.ExternalId)
                        .SetProperty(e => e.Title, model.Title)
                        .SetProperty(e => e.State, model.State)
                        .SetProperty(e => e.OutputPath, model.OutputPath)
                        .SetProperty(e => e.FailureReason, model.FailureReason)
                        .SetProperty(e => e.DateCompleted, model.DateCompleted)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Integrations,
                    operation: "Update",
                    entityId: model.Id,
                    entityType: nameof(Download),
                    description: $"Updated download {model.Title}",
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
                "Error updating download {DownloadId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating download",
                ErrorCodes.DbDownloadError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateStateAsync(
        Guid id,
        DownloadState state,
        string? outputPath,
        string? failureReason,
        DateTimeOffset? dateCompleted,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.Downloads
                .Where(d => d.Id == id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.State, state)
                        .SetProperty(e => e.OutputPath, outputPath)
                        .SetProperty(e => e.FailureReason, failureReason)
                        .SetProperty(e => e.DateCompleted, dateCompleted)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error updating download state {DownloadId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating download state",
                ErrorCodes.DbDownloadError,
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

            var entity = await context.Downloads
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            var deleted = await context.Downloads
                .Where(d => d.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Integrations,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(Download),
                    description: $"Deleted download {entity.Title}",
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
                "Error deleting download {DownloadId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error deleting download",
                ErrorCodes.DbDownloadError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<int>> DeleteByDownloadClientIdAsync(
        Guid downloadClientId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.Downloads
                .Where(d => d.DownloadClientId == downloadClientId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Integrations,
                    operation: "Delete",
                    entityId: downloadClientId,
                    entityType: nameof(Download),
                    description: $"Deleted {deleted} downloads for download client",
                    userId: deletedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(deleted);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error deleting downloads for download client {DownloadClientId} ({ExErrorCode})",
                downloadClientId,
                errorCode ?? "");
            return Result.Fail<int>(
                "Error deleting downloads",
                ErrorCodes.DbDownloadError,
                errorCode);
        }
    }
}
