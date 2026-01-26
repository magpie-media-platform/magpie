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
using Magpie.Models.Indexers;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ReleaseCategoryEntity = Magpie.Data.Database.Entities.ReleaseCategory;
using ReleaseLanguageEntity = Magpie.Data.Database.Entities.ReleaseLanguage;
using ReleasePropertyEntity = Magpie.Data.Database.Entities.ReleaseProperty;
using ReleaseTagEntity = Magpie.Data.Database.Entities.ReleaseTag;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing releases.
/// </summary>
public sealed class ReleaseRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<ReleaseRepository> logger) : IReleaseRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Release>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.Releases
                .AsNoTracking()
                .Include(r => r.Categories)
                .Include(r => r.Languages)
                .Include(r => r.Properties)
                .Include(r => r.Tags)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Release>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting releases ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<Release>>(
                "Error retrieving releases",
                ErrorCodes.DbReleaseError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Release?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from r in context.Releases
                    .AsNoTracking()
                    .Include(r => r.Categories)
                    .Include(r => r.Languages)
                    .Include(r => r.Properties)
                    .Include(r => r.Tags)
                where r.Id == id
                select r)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting release {ReleaseId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<Release?>(
                "Error retrieving release",
                ErrorCodes.DbReleaseError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Release>>> GetByIndexerIdAsync(
        Guid indexerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from r in context.Releases
                    .AsNoTracking()
                    .Include(r => r.Categories)
                    .Include(r => r.Languages)
                    .Include(r => r.Properties)
                    .Include(r => r.Tags)
                where r.IndexerId == indexerId
                select r)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Release>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting releases for indexer {IndexerId} ({ExErrorCode})",
                indexerId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<Release>>(
                "Error retrieving releases",
                ErrorCodes.DbReleaseError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Release?>> GetByExternalIdAsync(
        Guid indexerId,
        string externalId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from r in context.Releases
                    .AsNoTracking()
                    .Include(r => r.Categories)
                    .Include(r => r.Languages)
                    .Include(r => r.Properties)
                    .Include(r => r.Tags)
                where r.IndexerId == indexerId && r.ExternalId == externalId
                select r)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting release by external ID {ExternalId} for indexer {IndexerId} ({ExErrorCode})",
                externalId,
                indexerId,
                errorCode ?? "");
            return Result.Fail<Release?>(
                "Error retrieving release",
                ErrorCodes.DbReleaseError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Release>>> GetByProviderAsync(
        string provider,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from r in context.Releases
                    .AsNoTracking()
                    .Include(r => r.Categories)
                    .Include(r => r.Languages)
                    .Include(r => r.Properties)
                    .Include(r => r.Tags)
                where r.Provider == provider
                select r)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Release>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting releases for provider {Provider} ({ExErrorCode})",
                provider,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<Release>>(
                "Error retrieving releases",
                ErrorCodes.DbReleaseError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Release>>> GetByInfoHashAsync(
        string infoHash,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from r in context.Releases
                    .AsNoTracking()
                    .Include(r => r.Categories)
                    .Include(r => r.Languages)
                    .Include(r => r.Properties)
                    .Include(r => r.Tags)
                where r.InfoHash == infoHash
                select r)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Release>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting releases by info hash {InfoHash} ({ExErrorCode})",
                infoHash,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<Release>>(
                "Error retrieving releases",
                ErrorCodes.DbReleaseError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Release>>> GetByTagAsync(
        string tagName,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from r in context.Releases
                    .AsNoTracking()
                    .Include(r => r.Categories)
                    .Include(r => r.Languages)
                    .Include(r => r.Properties)
                    .Include(r => r.Tags)
                where r.Tags.Any(t => t.TagName == tagName)
                select r)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Release>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting releases by tag {TagName} ({ExErrorCode})",
                tagName,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<Release>>(
                "Error retrieving releases",
                ErrorCodes.DbReleaseError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Release>> CreateAsync(
        Release model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = model.CreateEntity(createdByUserId);

            foreach (var category in model.Categories)
            {
                entity.Categories.Add(new ReleaseCategoryEntity { Category = category });
            }

            foreach (var language in model.Languages)
            {
                entity.Languages.Add(new ReleaseLanguageEntity { Language = language });
            }

            if (model.Properties is not null)
            {
                foreach (var kvp in model.Properties)
                {
                    entity.Properties.Add(new ReleasePropertyEntity { Key = kvp.Key, Value = kvp.Value });
                }
            }

            foreach (var tag in model.Tags)
            {
                entity.Tags.Add(new ReleaseTagEntity { TagName = tag });
            }

            context.Releases.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Integrations,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(Release),
                description: $"Created release {model.Title}",
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
                "Error creating release {ReleaseTitle} ({ExErrorCode})",
                model.Title,
                errorCode ?? "");
            return Result.Fail<Release>(
                "Error creating release",
                ErrorCodes.DbReleaseError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        Release model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.Releases
                .Where(r => r.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.IndexerId, model.IndexerId)
                        .SetProperty(e => e.ExternalId, model.ExternalId)
                        .SetProperty(e => e.Provider, model.Provider)
                        .SetProperty(e => e.Title, model.Title)
                        .SetProperty(e => e.PublishDate, model.PublishDate)
                        .SetProperty(e => e.Size, model.Size)
                        .SetProperty(e => e.InfoHash, model.InfoHash)
                        .SetProperty(e => e.DownloadUrl, model.DownloadUrl)
                        .SetProperty(e => e.InfoUrl, model.InfoUrl)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                await context.ReleaseCategories
                    .Where(c => c.ReleaseId == model.Id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var category in model.Categories)
                {
                    context.ReleaseCategories.Add(new ReleaseCategoryEntity
                    {
                        ReleaseId = model.Id,
                        Category = category
                    });
                }
                await context.ReleaseLanguages
                    .Where(l => l.ReleaseId == model.Id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var language in model.Languages)
                {
                    context.ReleaseLanguages.Add(new ReleaseLanguageEntity
                    {
                        ReleaseId = model.Id,
                        Language = language
                    });
                }
                await context.ReleaseProperties
                    .Where(p => p.ReleaseId == model.Id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (model.Properties is not null)
                {
                    foreach (var kvp in model.Properties)
                    {
                        context.ReleaseProperties.Add(new ReleasePropertyEntity
                        {
                            ReleaseId = model.Id,
                            Key = kvp.Key,
                            Value = kvp.Value
                        });
                    }
                }
                var existingTags = await context.ReleaseTags
                    .Where(t => t.ReleaseId == model.Id)
                    .Select(t => t.TagName)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                await context.ReleaseTags
                    .Where(t => t.ReleaseId == model.Id)
                    .Where(t => !model.Tags.Contains(t.TagName))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var tag in model.Tags)
                {
                    if (!existingTags.Contains(tag))
                    {
                        context.ReleaseTags.Add(new ReleaseTagEntity
                        {
                            ReleaseId = model.Id,
                            TagName = tag
                        });
                    }
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Integrations,
                    operation: "Update",
                    entityId: model.Id,
                    entityType: nameof(Release),
                    description: $"Updated release {model.Title}",
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
                "Error updating release {ReleaseId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating release",
                ErrorCodes.DbReleaseError,
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

            var entity = await context.Releases
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            await context.ReleaseCategories
                .Where(c => c.ReleaseId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await context.ReleaseLanguages
                .Where(l => l.ReleaseId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await context.ReleaseProperties
                .Where(p => p.ReleaseId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await context.ReleaseTags
                .Where(t => t.ReleaseId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.Releases
                .Where(r => r.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Integrations,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(Release),
                    description: $"Deleted release {entity.Title}",
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
                "Error deleting release {ReleaseId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error deleting release",
                ErrorCodes.DbReleaseError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<int>> DeleteByIndexerIdAsync(
        Guid indexerId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var releaseIds = await context.Releases
                .Where(r => r.IndexerId == indexerId)
                .Select(r => r.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            await context.ReleaseCategories
                .Where(c => releaseIds.Contains(c.ReleaseId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await context.ReleaseLanguages
                .Where(l => releaseIds.Contains(l.ReleaseId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await context.ReleaseProperties
                .Where(p => releaseIds.Contains(p.ReleaseId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await context.ReleaseTags
                .Where(t => releaseIds.Contains(t.ReleaseId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.Releases
                .Where(r => r.IndexerId == indexerId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Integrations,
                    operation: "Delete",
                    entityId: indexerId,
                    entityType: nameof(Release),
                    description: $"Deleted {deleted} releases for indexer",
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
                "Error deleting releases for indexer {IndexerId} ({ExErrorCode})",
                indexerId,
                errorCode ?? "");
            return Result.Fail<int>(
                "Error deleting releases",
                ErrorCodes.DbReleaseError,
                errorCode);
        }
    }
}
