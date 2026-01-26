using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.History;
using Magpie.Models.Metadata;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MetadataContributionEntity = Magpie.Data.Database.Entities.MetadataContribution;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing metadata containers and contributions.
/// </summary>
public sealed class MetadataRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<MetadataRepository> logger) : IMetadataRepository
{
    /// <inheritdoc />
    public async Task<Result<MetadataContainer?>> GetContainerAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from mc in context.MetadataContainers.AsNoTracking()
                    .Include(mc => mc.Contributions).ThenInclude(c => c.CustomMetadataValues)
                    .Include(mc => mc.Contributions).ThenInclude(c => c.ExternalIds)
                    .Include(mc => mc.Contributions).ThenInclude(c => c.References)
                    .Include(mc => mc.Contributions).ThenInclude(c => c.Relationships)
                    .Include(mc => mc.Contributions).ThenInclude(c => c.Images)
                    .Include(mc => mc.Contributions).ThenInclude(c => c.Previews)
                    .Include(mc => mc.Contributions).ThenInclude(c => c.AlternateTexts)
                    .Include(mc => mc.Contributions).ThenInclude(c => c.ContentRatings)
                    .Include(mc => mc.Contributions).ThenInclude(c => c.Awards)
                    .Include(mc => mc.Contributions).ThenInclude(c => c.Genres)
                where mc.Id == id
                select mc)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting metadata container {ContainerId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<MetadataContainer?>(
                "Error retrieving metadata container",
                ErrorCodes.DbMetadataError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<MetadataContribution>> AddContributionAsync(
        Guid containerId,
        MetadataContribution contribution,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var containerExists = await context.MetadataContainers
                .AnyAsync(mc => mc.Id == containerId, cancellationToken)
                .ConfigureAwait(false);

            if (!containerExists)
            {
                return Result.Fail<MetadataContribution>(
                    "Metadata container not found",
                    ErrorCodes.DbMetadataError);
            }

            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = new MetadataContributionEntity
            {
                MetadataContainerId = containerId,
                Ordinal = contribution.Ordinal,
                Locked = contribution.Locked,
                Provider = contribution.Provider,
                DateAdded = DateTimeOffset.UtcNow,
                DateLastRefreshed = contribution.DateLastRefreshed,
                Attribution = contribution.Attribution,
                Title = contribution.Title,
                OriginalTitle = contribution.OriginalTitle,
                SortTitle = contribution.SortTitle,
                Language = contribution.Language,
                Overview = contribution.Overview,
                PublishDate = contribution.PublishDate,
                Duration = contribution.Duration,
                ProductionStatus = contribution.ProductionStatus,
                Established = contribution.Established,
                Dissolved = contribution.Dissolved,
                BirthDate = contribution.BirthDate,
                DeathDate = contribution.DeathDate
            };

            AddCollectionEntities(entity, contribution);

            context.MetadataContributions.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Create",
                entityId: entity.Id,
                parentEntityId: containerId,
                entityType: nameof(MetadataContribution),
                description: $"Added metadata contribution from provider {contribution.Provider ?? "unknown"}",
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
                "Error adding contribution to container {ContainerId} ({ExErrorCode})",
                containerId,
                errorCode ?? "");
            return Result.Fail<MetadataContribution>(
                "Error adding metadata contribution",
                ErrorCodes.DbMetadataError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchContributionAsync(
        MetadataContributionPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.MetadataContributions
                .Include(c => c.CustomMetadataValues)
                .Include(c => c.ExternalIds)
                .Include(c => c.References)
                .Include(c => c.Relationships)
                .Include(c => c.Images)
                .Include(c => c.Previews)
                .Include(c => c.AlternateTexts)
                .Include(c => c.ContentRatings)
                .Include(c => c.Awards)
                .Include(c => c.Genres)
                .FirstOrDefaultAsync(c => c.Id == patch.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            ApplyScalarPatches(entity, patch);
            await ApplyCollectionPatchesAsync(entity, patch, cancellationToken).ConfigureAwait(false);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Patch",
                entityId: patch.Id,
                parentEntityId: entity.MetadataContainerId,
                entityType: nameof(MetadataContribution),
                description: $"Patched metadata contribution from provider {entity.Provider ?? "unknown"}",
                userId: modifiedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(true);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error patching contribution {ContributionId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error patching metadata contribution",
                ErrorCodes.DbMetadataError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> DeleteContributionAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await context.MetadataContributions
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            if (entity.Locked)
            {
                return Result.Fail<bool>(
                    "Cannot delete locked metadata contribution",
                    ErrorCodes.DbMetadataError);
            }

            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var containerId = entity.MetadataContainerId;
            var provider = entity.Provider;

            var deleted = await context.MetadataContributions
                .Where(c => c.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: id,
                    parentEntityId: containerId,
                    entityType: nameof(MetadataContribution),
                    description: $"Deleted metadata contribution from provider {provider ?? "unknown"}",
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
                "Error deleting contribution {ContributionId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error deleting metadata contribution",
                ErrorCodes.DbMetadataError,
                errorCode);
        }
    }

    private static void AddCollectionEntities(
        MetadataContributionEntity entity,
        MetadataContribution contribution)
    {
        foreach (var kvp in contribution.CustomMetadata)
        {
            entity.CustomMetadataValues.Add(new()
            {
                Key = kvp.Key,
                Value = kvp.Value
            });
        }

        if (contribution.ExternalIds is not null)
        {
            foreach (var externalId in contribution.ExternalIds)
            {
                entity.ExternalIds.Add(new()
                {
                    Ordinal = externalId.Ordinal,
                    Value = externalId.Value,
                    Attribution = externalId.Attribution
                });
            }
        }

        if (contribution.References is not null)
        {
            foreach (var reference in contribution.References)
            {
                entity.References.Add(new()
                {
                    Ordinal = reference.Ordinal,
                    Kind = reference.Kind,
                    Url = reference.Url,
                    DisplayName = reference.DisplayName,
                    Language = reference.Language
                });
            }
        }

        if (contribution.Relationships is not null)
        {
            foreach (var relationship in contribution.Relationships)
            {
                entity.Relationships.Add(new()
                {
                    SubjectId = relationship.RelatedMetadataId,
                    Ordinal = relationship.Ordinal,
                    Relation = relationship.Relation,
                    Qualifier = relationship.Qualifier
                });
            }
        }

        if (contribution.Images is not null)
        {
            foreach (var image in contribution.Images)
            {
                entity.Images.Add(new()
                {
                    Ordinal = image.Ordinal,
                    ImageType = image.ImageType,
                    MimeType = image.MimeType,
                    Url = image.Url,
                    Attribution = image.Attribution,
                    Language = image.Language,
                    Width = image.Width,
                    Height = image.Height
                });
            }
        }

        if (contribution.Previews is not null)
        {
            foreach (var preview in contribution.Previews)
            {
                entity.Previews.Add(new()
                {
                    Ordinal = preview.Ordinal,
                    PreviewType = preview.PreviewType,
                    MimeType = preview.MimeType,
                    Url = preview.Url,
                    Attribution = preview.Attribution,
                    Title = preview.Title,
                    ReleaseDate = preview.ReleaseDate,
                    Language = preview.Language
                });
            }
        }

        if (contribution.AlternateTexts is not null)
        {
            foreach (var alternateText in contribution.AlternateTexts)
            {
                entity.AlternateTexts.Add(new()
                {
                    Ordinal = alternateText.Ordinal,
                    Type = alternateText.Type,
                    Language = alternateText.Language,
                    Text = alternateText.Text
                });
            }
        }

        if (contribution.ContentRatings is not null)
        {
            foreach (var contentRating in contribution.ContentRatings)
            {
                entity.ContentRatings.Add(new()
                {
                    Ordinal = contentRating.Ordinal,
                    RatingsBody = contentRating.RatingsBody,
                    Value = contentRating.Value
                });
            }
        }

        if (contribution.Awards is not null)
        {
            foreach (var award in contribution.Awards)
            {
                entity.Awards.Add(new()
                {
                    Ordinal = award.Ordinal,
                    AwardBody = award.AwardBody,
                    Name = award.Name,
                    Category = award.Category,
                    Status = award.Status,
                    Year = award.Year,
                    Subject = award.Subject,
                    Language = award.Language,
                    Country = award.Country
                });
            }
        }

        if (contribution.Genres is not null)
        {
            foreach (var genre in contribution.Genres)
            {
                entity.Genres.Add(new()
                {
                    Ordinal = genre.Ordinal,
                    GenreName = genre.GenreName
                });
            }
        }
    }

    private static void ApplyScalarPatches(
        MetadataContributionEntity entity,
        MetadataContributionPatch patch)
    {
        if (patch.Ordinal is not null)
        {
            entity.Ordinal = patch.Ordinal.Value;
        }

        if (patch.Locked is not null)
        {
            entity.Locked = patch.Locked.Value;
        }

        if (patch.Provider is not null)
        {
            entity.Provider = patch.Provider.Value;
        }

        if (patch.Attribution is not null)
        {
            entity.Attribution = patch.Attribution.Value;
        }

        if (patch.Title is not null)
        {
            entity.Title = patch.Title.Value;
        }

        if (patch.OriginalTitle is not null)
        {
            entity.OriginalTitle = patch.OriginalTitle.Value;
        }

        if (patch.SortTitle is not null)
        {
            entity.SortTitle = patch.SortTitle.Value;
        }

        if (patch.Language is not null)
        {
            entity.Language = patch.Language.Value;
        }

        if (patch.Overview is not null)
        {
            entity.Overview = patch.Overview.Value;
        }

        if (patch.PublishDate is not null)
        {
            entity.PublishDate = patch.PublishDate.Value;
        }

        if (patch.Duration is not null)
        {
            entity.Duration = patch.Duration.Value;
        }

        if (patch.ProductionStatus is not null)
        {
            entity.ProductionStatus = patch.ProductionStatus.Value;
        }

        if (patch.Established is not null)
        {
            entity.Established = patch.Established.Value;
        }

        if (patch.Dissolved is not null)
        {
            entity.Dissolved = patch.Dissolved.Value;
        }

        if (patch.BirthDate is not null)
        {
            entity.BirthDate = patch.BirthDate.Value;
        }

        if (patch.DeathDate is not null)
        {
            entity.DeathDate = patch.DeathDate.Value;
        }

        if (patch.CustomMetadata is not null)
        {
            entity.CustomMetadataValues.Clear();
            foreach (var kvp in patch.CustomMetadata.Value!)
            {
                entity.CustomMetadataValues.Add(new()
                {
                    Key = kvp.Key,
                    Value = kvp.Value
                });
            }
        }
    }

    private async Task ApplyCollectionPatchesAsync(
        MetadataContributionEntity entity,
        MetadataContributionPatch patch,
        CancellationToken cancellationToken)
    {
        if (patch.ExternalIds is not null)
        {
            await ApplyExternalIdsPatchAsync(entity, patch.ExternalIds, cancellationToken).ConfigureAwait(false);
        }

        if (patch.References is not null)
        {
            await ApplyReferencesPatchAsync(entity, patch.References, cancellationToken).ConfigureAwait(false);
        }

        if (patch.Relationships is not null)
        {
            await ApplyRelationshipsPatchAsync(entity, patch.Relationships, cancellationToken).ConfigureAwait(false);
        }

        if (patch.Images is not null)
        {
            await ApplyImagesPatchAsync(entity, patch.Images, cancellationToken).ConfigureAwait(false);
        }

        if (patch.Previews is not null)
        {
            await ApplyPreviewsPatchAsync(entity, patch.Previews, cancellationToken).ConfigureAwait(false);
        }

        if (patch.AlternateTexts is not null)
        {
            await ApplyAlternateTextsPatchAsync(entity, patch.AlternateTexts, cancellationToken).ConfigureAwait(false);
        }

        if (patch.ContentRatings is not null)
        {
            await ApplyContentRatingsPatchAsync(entity, patch.ContentRatings, cancellationToken).ConfigureAwait(false);
        }

        if (patch.Awards is not null)
        {
            await ApplyAwardsPatchAsync(entity, patch.Awards, cancellationToken).ConfigureAwait(false);
        }

        if (patch.Genres is not null)
        {
            await ApplyGenresPatchAsync(entity, patch.Genres, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyExternalIdsPatchAsync(
        MetadataContributionEntity entity,
        CollectionPatch<ExternalIdMetadata, Guid, ExternalIdMetadataPatch> patch,
        CancellationToken cancellationToken)
    {
        if (patch.Remove is not null)
        {
            await context.ExternalIds
                .Where(e => e.MetadataContributionId == entity.Id)
                .Where(e => patch.Remove.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (patch.Add is not null)
        {
            foreach (var item in patch.Add)
            {
                entity.ExternalIds.Add(new()
                {
                    Ordinal = item.Ordinal,
                    Value = item.Value,
                    Attribution = item.Attribution
                });
            }
        }

        if (patch.Update is not null)
        {
            var existingById = entity.ExternalIds.ToDictionary(e => e.Id);
            foreach (var itemPatch in patch.Update)
            {
                if (existingById.TryGetValue(itemPatch.Id, out var existing))
                {
                    if (itemPatch.Ordinal is not null)
                    {
                        existing.Ordinal = itemPatch.Ordinal.Value;
                    }

                    if (itemPatch.Value is not null)
                    {
                        existing.Value = itemPatch.Value.Value!;
                    }

                    if (itemPatch.Attribution is not null)
                    {
                        existing.Attribution = itemPatch.Attribution.Value;
                    }
                }
            }
        }
    }

    private async Task ApplyReferencesPatchAsync(
        MetadataContributionEntity entity,
        CollectionPatch<ReferenceMetadata, Guid, ReferenceMetadataPatch> patch,
        CancellationToken cancellationToken)
    {
        if (patch.Remove is not null)
        {
            await context.References
                .Where(e => e.MetadataContributionId == entity.Id)
                .Where(e => patch.Remove.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (patch.Add is not null)
        {
            foreach (var item in patch.Add)
            {
                entity.References.Add(new()
                {
                    Ordinal = item.Ordinal,
                    Kind = item.Kind,
                    Url = item.Url,
                    DisplayName = item.DisplayName,
                    Language = item.Language
                });
            }
        }

        if (patch.Update is not null)
        {
            var existingById = entity.References.ToDictionary(e => e.Id);
            foreach (var itemPatch in patch.Update)
            {
                if (existingById.TryGetValue(itemPatch.Id, out var existing))
                {
                    if (itemPatch.Ordinal is not null)
                    {
                        existing.Ordinal = itemPatch.Ordinal.Value;
                    }

                    if (itemPatch.Kind is not null)
                    {
                        existing.Kind = itemPatch.Kind.Value!;
                    }

                    if (itemPatch.Url is not null)
                    {
                        existing.Url = itemPatch.Url.Value!;
                    }

                    if (itemPatch.DisplayName is not null)
                    {
                        existing.DisplayName = itemPatch.DisplayName.Value;
                    }

                    if (itemPatch.Language is not null)
                    {
                        existing.Language = itemPatch.Language.Value;
                    }
                }
            }
        }
    }

    private async Task ApplyRelationshipsPatchAsync(
        MetadataContributionEntity entity,
        CollectionPatch<RelationshipMetadata, Guid, RelationshipMetadataPatch> patch,
        CancellationToken cancellationToken)
    {
        if (patch.Remove is not null)
        {
            await context.Relationships
                .Where(e => e.MetadataContributionId == entity.Id)
                .Where(e => patch.Remove.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (patch.Add is not null)
        {
            foreach (var item in patch.Add)
            {
                entity.Relationships.Add(new()
                {
                    SubjectId = item.RelatedMetadataId,
                    Ordinal = item.Ordinal,
                    Relation = item.Relation,
                    Qualifier = item.Qualifier
                });
            }
        }

        if (patch.Update is not null)
        {
            var existingById = entity.Relationships.ToDictionary(e => e.Id);
            foreach (var itemPatch in patch.Update)
            {
                if (existingById.TryGetValue(itemPatch.Id, out var existing))
                {
                    if (itemPatch.Ordinal is not null)
                    {
                        existing.Ordinal = itemPatch.Ordinal.Value;
                    }

                    if (itemPatch.RelatedMetadataId is not null)
                    {
                        existing.SubjectId = itemPatch.RelatedMetadataId.Value;
                    }

                    if (itemPatch.Relation is not null)
                    {
                        existing.Relation = itemPatch.Relation.Value!;
                    }

                    if (itemPatch.Qualifier is not null)
                    {
                        existing.Qualifier = itemPatch.Qualifier.Value;
                    }
                }
            }
        }
    }

    private async Task ApplyImagesPatchAsync(
        MetadataContributionEntity entity,
        CollectionPatch<ImageMetadata, Guid, ImageMetadataPatch> patch,
        CancellationToken cancellationToken)
    {
        if (patch.Remove is not null)
        {
            await context.Images
                .Where(e => e.MetadataContributionId == entity.Id)
                .Where(e => patch.Remove.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (patch.Add is not null)
        {
            foreach (var item in patch.Add)
            {
                entity.Images.Add(new()
                {
                    Ordinal = item.Ordinal,
                    ImageType = item.ImageType,
                    MimeType = item.MimeType,
                    Url = item.Url,
                    Attribution = item.Attribution,
                    Language = item.Language,
                    Width = item.Width,
                    Height = item.Height
                });
            }
        }

        if (patch.Update is not null)
        {
            var existingById = entity.Images.ToDictionary(e => e.Id);
            foreach (var itemPatch in patch.Update)
            {
                if (existingById.TryGetValue(itemPatch.Id, out var existing))
                {
                    if (itemPatch.Ordinal is not null)
                    {
                        existing.Ordinal = itemPatch.Ordinal.Value;
                    }

                    if (itemPatch.ImageType is not null)
                    {
                        existing.ImageType = itemPatch.ImageType.Value!;
                    }

                    if (itemPatch.MimeType is not null)
                    {
                        existing.MimeType = itemPatch.MimeType.Value;
                    }

                    if (itemPatch.Url is not null)
                    {
                        existing.Url = itemPatch.Url.Value!;
                    }

                    if (itemPatch.Attribution is not null)
                    {
                        existing.Attribution = itemPatch.Attribution.Value;
                    }

                    if (itemPatch.Language is not null)
                    {
                        existing.Language = itemPatch.Language.Value;
                    }

                    if (itemPatch.Width is not null)
                    {
                        existing.Width = itemPatch.Width.Value;
                    }

                    if (itemPatch.Height is not null)
                    {
                        existing.Height = itemPatch.Height.Value;
                    }
                }
            }
        }
    }

    private async Task ApplyPreviewsPatchAsync(
        MetadataContributionEntity entity,
        CollectionPatch<PreviewMetadata, Guid, PreviewMetadataPatch> patch,
        CancellationToken cancellationToken)
    {
        if (patch.Remove is not null)
        {
            await context.Previews
                .Where(e => e.MetadataContributionId == entity.Id)
                .Where(e => patch.Remove.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (patch.Add is not null)
        {
            foreach (var item in patch.Add)
            {
                entity.Previews.Add(new()
                {
                    Ordinal = item.Ordinal,
                    PreviewType = item.PreviewType,
                    MimeType = item.MimeType,
                    Url = item.Url,
                    Attribution = item.Attribution,
                    Title = item.Title,
                    ReleaseDate = item.ReleaseDate,
                    Language = item.Language
                });
            }
        }

        if (patch.Update is not null)
        {
            var existingById = entity.Previews.ToDictionary(e => e.Id);
            foreach (var itemPatch in patch.Update)
            {
                if (existingById.TryGetValue(itemPatch.Id, out var existing))
                {
                    if (itemPatch.Ordinal is not null)
                    {
                        existing.Ordinal = itemPatch.Ordinal.Value;
                    }

                    if (itemPatch.PreviewType is not null)
                    {
                        existing.PreviewType = itemPatch.PreviewType.Value!;
                    }

                    if (itemPatch.MimeType is not null)
                    {
                        existing.MimeType = itemPatch.MimeType.Value;
                    }

                    if (itemPatch.Url is not null)
                    {
                        existing.Url = itemPatch.Url.Value!;
                    }

                    if (itemPatch.Attribution is not null)
                    {
                        existing.Attribution = itemPatch.Attribution.Value;
                    }

                    if (itemPatch.Title is not null)
                    {
                        existing.Title = itemPatch.Title.Value;
                    }

                    if (itemPatch.ReleaseDate is not null)
                    {
                        existing.ReleaseDate = itemPatch.ReleaseDate.Value;
                    }

                    if (itemPatch.Language is not null)
                    {
                        existing.Language = itemPatch.Language.Value;
                    }
                }
            }
        }
    }

    private async Task ApplyAlternateTextsPatchAsync(
        MetadataContributionEntity entity,
        CollectionPatch<AlternateTextMetadata, Guid, AlternateTextMetadataPatch> patch,
        CancellationToken cancellationToken)
    {
        if (patch.Remove is not null)
        {
            await context.AlternateTexts
                .Where(e => e.MetadataContributionId == entity.Id)
                .Where(e => patch.Remove.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (patch.Add is not null)
        {
            foreach (var item in patch.Add)
            {
                entity.AlternateTexts.Add(new()
                {
                    Ordinal = item.Ordinal,
                    Type = item.Type,
                    Language = item.Language,
                    Text = item.Text
                });
            }
        }

        if (patch.Update is not null)
        {
            var existingById = entity.AlternateTexts.ToDictionary(e => e.Id);
            foreach (var itemPatch in patch.Update)
            {
                if (existingById.TryGetValue(itemPatch.Id, out var existing))
                {
                    if (itemPatch.Ordinal is not null)
                    {
                        existing.Ordinal = itemPatch.Ordinal.Value;
                    }

                    if (itemPatch.Type is not null)
                    {
                        existing.Type = itemPatch.Type.Value!;
                    }

                    if (itemPatch.Language is not null)
                    {
                        existing.Language = itemPatch.Language.Value!;
                    }

                    if (itemPatch.Text is not null)
                    {
                        existing.Text = itemPatch.Text.Value!;
                    }
                }
            }
        }
    }

    private async Task ApplyContentRatingsPatchAsync(
        MetadataContributionEntity entity,
        CollectionPatch<ContentRatingMetadata, Guid, ContentRatingMetadataPatch> patch,
        CancellationToken cancellationToken)
    {
        if (patch.Remove is not null)
        {
            await context.ContentRatings
                .Where(e => e.MetadataContributionId == entity.Id)
                .Where(e => patch.Remove.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (patch.Add is not null)
        {
            foreach (var item in patch.Add)
            {
                entity.ContentRatings.Add(new()
                {
                    Ordinal = item.Ordinal,
                    RatingsBody = item.RatingsBody,
                    Value = item.Value
                });
            }
        }

        if (patch.Update is not null)
        {
            var existingById = entity.ContentRatings.ToDictionary(e => e.Id);
            foreach (var itemPatch in patch.Update)
            {
                if (existingById.TryGetValue(itemPatch.Id, out var existing))
                {
                    if (itemPatch.Ordinal is not null)
                    {
                        existing.Ordinal = itemPatch.Ordinal.Value;
                    }

                    if (itemPatch.RatingsBody is not null)
                    {
                        existing.RatingsBody = itemPatch.RatingsBody.Value!;
                    }

                    if (itemPatch.Value is not null)
                    {
                        existing.Value = itemPatch.Value.Value!;
                    }
                }
            }
        }
    }

    private async Task ApplyAwardsPatchAsync(
        MetadataContributionEntity entity,
        CollectionPatch<AwardMetadata, Guid, AwardMetadataPatch> patch,
        CancellationToken cancellationToken)
    {
        if (patch.Remove is not null)
        {
            await context.Awards
                .Where(e => e.MetadataContributionId == entity.Id)
                .Where(e => patch.Remove.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (patch.Add is not null)
        {
            foreach (var item in patch.Add)
            {
                entity.Awards.Add(new()
                {
                    Ordinal = item.Ordinal,
                    AwardBody = item.AwardBody,
                    Name = item.Name,
                    Category = item.Category,
                    Status = item.Status,
                    Year = item.Year,
                    Subject = item.Subject,
                    Language = item.Language,
                    Country = item.Country
                });
            }
        }

        if (patch.Update is not null)
        {
            var existingById = entity.Awards.ToDictionary(e => e.Id);
            foreach (var itemPatch in patch.Update)
            {
                if (existingById.TryGetValue(itemPatch.Id, out var existing))
                {
                    if (itemPatch.Ordinal is not null)
                    {
                        existing.Ordinal = itemPatch.Ordinal.Value;
                    }

                    if (itemPatch.AwardBody is not null)
                    {
                        existing.AwardBody = itemPatch.AwardBody.Value!;
                    }

                    if (itemPatch.Name is not null)
                    {
                        existing.Name = itemPatch.Name.Value!;
                    }

                    if (itemPatch.Category is not null)
                    {
                        existing.Category = itemPatch.Category.Value;
                    }

                    if (itemPatch.Status is not null)
                    {
                        existing.Status = itemPatch.Status.Value;
                    }

                    if (itemPatch.Year is not null)
                    {
                        existing.Year = itemPatch.Year.Value;
                    }

                    if (itemPatch.Subject is not null)
                    {
                        existing.Subject = itemPatch.Subject.Value;
                    }

                    if (itemPatch.Language is not null)
                    {
                        existing.Language = itemPatch.Language.Value;
                    }

                    if (itemPatch.Country is not null)
                    {
                        existing.Country = itemPatch.Country.Value;
                    }
                }
            }
        }
    }

    private async Task ApplyGenresPatchAsync(
        MetadataContributionEntity entity,
        CollectionPatch<GenreMetadata, Guid, GenreMetadataPatch> patch,
        CancellationToken cancellationToken)
    {
        if (patch.Remove is not null)
        {
            await context.Genres
                .Where(e => e.MetadataContributionId == entity.Id)
                .Where(e => patch.Remove.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (patch.Add is not null)
        {
            foreach (var item in patch.Add)
            {
                entity.Genres.Add(new()
                {
                    Ordinal = item.Ordinal,
                    GenreName = item.GenreName
                });
            }
        }

        if (patch.Update is not null)
        {
            var existingById = entity.Genres.ToDictionary(e => e.Id);
            foreach (var itemPatch in patch.Update)
            {
                if (existingById.TryGetValue(itemPatch.Id, out var existing))
                {
                    if (itemPatch.Ordinal is not null)
                    {
                        existing.Ordinal = itemPatch.Ordinal.Value;
                    }

                    if (itemPatch.Genre is not null)
                    {
                        existing.GenreName = itemPatch.Genre.Value!;
                    }
                }
            }
        }
    }
}
