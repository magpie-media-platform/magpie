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

using IndexerDefinitionCategoryEntity = Magpie.Data.Database.Entities.IndexerDefinitionCategory;
using IndexerDefinitionLanguageEntity = Magpie.Data.Database.Entities.IndexerDefinitionLanguage;
using IndexerDefinitionTagEntity = Magpie.Data.Database.Entities.IndexerDefinitionTag;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing indexer definitions.
/// </summary>
public sealed class IndexerDefinitionRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<IndexerDefinitionRepository> logger) : IIndexerDefinitionRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<IndexerDefinition>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var definitions = await context.IndexerDefinitions
                .AsNoTracking()
                .Include(d => d.Languages)
                .Include(d => d.Categories)
                .Include(d => d.Tags)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = definitions.Select(d => d.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<IndexerDefinition>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting indexer definitions ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<IndexerDefinition>>(
                "Error retrieving indexer definitions",
                ErrorCodes.DbIndexerDefinitionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IndexerDefinition?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var definition = await (
                from d in context.IndexerDefinitions
                    .AsNoTracking()
                    .Include(d => d.Languages)
                    .Include(d => d.Categories)
                    .Include(d => d.Tags)
                where d.Id == id
                select d)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(definition?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting indexer definition {IndexerDefinitionId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<IndexerDefinition?>(
                "Error retrieving indexer definition",
                ErrorCodes.DbIndexerDefinitionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IndexerDefinition?>> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var definition = await (
                from d in context.IndexerDefinitions
                    .AsNoTracking()
                    .Include(d => d.Languages)
                    .Include(d => d.Categories)
                    .Include(d => d.Tags)
                where d.Name == name
                select d)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(definition?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting indexer definition {IndexerDefinitionName} ({ExErrorCode})",
                name,
                errorCode ?? "");
            return Result.Fail<IndexerDefinition?>(
                "Error retrieving indexer definition",
                ErrorCodes.DbIndexerDefinitionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<IndexerDefinition>>> GetByFeedIdAsync(
        Guid feedId,
        CancellationToken cancellationToken)
    {
        try
        {
            var definitions = await (
                from d in context.IndexerDefinitions
                    .AsNoTracking()
                    .Include(d => d.Languages)
                    .Include(d => d.Categories)
                    .Include(d => d.Tags)
                where d.FeedId == feedId
                select d)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = definitions.Select(d => d.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<IndexerDefinition>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting indexer definitions for feed {FeedId} ({ExErrorCode})",
                feedId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<IndexerDefinition>>(
                "Error retrieving indexer definitions",
                ErrorCodes.DbIndexerDefinitionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<IndexerDefinition>>> GetByProviderAsync(
        string provider,
        CancellationToken cancellationToken)
    {
        try
        {
            var definitions = await (
                from d in context.IndexerDefinitions
                    .AsNoTracking()
                    .Include(d => d.Languages)
                    .Include(d => d.Categories)
                    .Include(d => d.Tags)
                where d.Provider == provider
                select d)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = definitions.Select(d => d.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<IndexerDefinition>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting indexer definitions for provider {Provider} ({ExErrorCode})",
                provider,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<IndexerDefinition>>(
                "Error retrieving indexer definitions",
                ErrorCodes.DbIndexerDefinitionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<IndexerDefinition>>> GetByAccessAsync(
        IndexerAccess access,
        CancellationToken cancellationToken)
    {
        try
        {
            var definitions = await (
                from d in context.IndexerDefinitions
                    .AsNoTracking()
                    .Include(d => d.Languages)
                    .Include(d => d.Categories)
                    .Include(d => d.Tags)
                where d.Access == access
                select d)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = definitions.Select(d => d.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<IndexerDefinition>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting indexer definitions with access {Access} ({ExErrorCode})",
                access,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<IndexerDefinition>>(
                "Error retrieving indexer definitions",
                ErrorCodes.DbIndexerDefinitionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IndexerDefinition>> CreateAsync(
        IndexerDefinition model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = model.CreateEntity(createdByUserId);

            foreach (var language in model.Languages)
            {
                entity.Languages.Add(new IndexerDefinitionLanguageEntity
                {
                    Language = language
                });
            }

            foreach (var category in model.Categories)
            {
                entity.Categories.Add(new IndexerDefinitionCategoryEntity
                {
                    Category = category
                });
            }

            foreach (var tag in model.Tags)
            {
                entity.Tags.Add(new IndexerDefinitionTagEntity
                {
                    TagName = tag
                });
            }

            context.IndexerDefinitions.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Integrations,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(IndexerDefinition),
                description: $"Created indexer definition {model.Name}",
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
                "Error creating indexer definition {IndexerDefinitionName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<IndexerDefinition>(
                "Error creating indexer definition",
                ErrorCodes.DbIndexerDefinitionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        IndexerDefinition model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.IndexerDefinitions
                .Where(d => d.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Name, model.Name)
                        .SetProperty(e => e.Provider, model.Provider)
                        .SetProperty(e => e.FeedId, model.FeedId)
                        .SetProperty(e => e.Access, model.Access)
                        .SetProperty(e => e.Description, model.Description)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                var existingLanguages = await context.IndexerDefinitionLanguages
                    .Where(l => l.IndexerDefinitionId == model.Id)
                    .Select(l => l.Language)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                await context.IndexerDefinitionLanguages
                    .Where(l => l.IndexerDefinitionId == model.Id)
                    .Where(l => !model.Languages.Contains(l.Language))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var language in model.Languages)
                {
                    if (!existingLanguages.Contains(language))
                    {
                        context.IndexerDefinitionLanguages.Add(new IndexerDefinitionLanguageEntity
                        {
                            IndexerDefinitionId = model.Id,
                            Language = language
                        });
                    }
                }
                var existingCategories = await context.IndexerDefinitionCategories
                    .Where(c => c.IndexerDefinitionId == model.Id)
                    .Select(c => c.Category)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                await context.IndexerDefinitionCategories
                    .Where(c => c.IndexerDefinitionId == model.Id)
                    .Where(c => !model.Categories.Contains(c.Category))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var category in model.Categories)
                {
                    if (!existingCategories.Contains(category))
                    {
                        context.IndexerDefinitionCategories.Add(new IndexerDefinitionCategoryEntity
                        {
                            IndexerDefinitionId = model.Id,
                            Category = category
                        });
                    }
                }
                var existingTags = await context.IndexerDefinitionTags
                    .Where(t => t.IndexerDefinitionId == model.Id)
                    .Select(t => t.TagName)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                await context.IndexerDefinitionTags
                    .Where(t => t.IndexerDefinitionId == model.Id)
                    .Where(t => !model.Tags.Contains(t.TagName))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var tag in model.Tags)
                {
                    if (!existingTags.Contains(tag))
                    {
                        context.IndexerDefinitionTags.Add(new IndexerDefinitionTagEntity
                        {
                            IndexerDefinitionId = model.Id,
                            TagName = tag
                        });
                    }
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Integrations,
                    operation: "Update",
                    entityId: model.Id,
                    entityType: nameof(IndexerDefinition),
                    description: $"Updated indexer definition {model.Name}",
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
                "Error updating indexer definition {IndexerDefinitionId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating indexer definition",
                ErrorCodes.DbIndexerDefinitionError,
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

            var entity = await context.IndexerDefinitions
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }
            await context.IndexerDefinitionLanguages
                .Where(l => l.IndexerDefinitionId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await context.IndexerDefinitionCategories
                .Where(c => c.IndexerDefinitionId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await context.IndexerDefinitionTags
                .Where(t => t.IndexerDefinitionId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.IndexerDefinitions
                .Where(d => d.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Integrations,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(IndexerDefinition),
                    description: $"Deleted indexer definition {entity.Name}",
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
                "Error deleting indexer definition {IndexerDefinitionId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error deleting indexer definition",
                ErrorCodes.DbIndexerDefinitionError,
                errorCode);
        }
    }
}
