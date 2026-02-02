using System;
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
/// Repository for managing metadata containers and contributions.
/// </summary>
public sealed class MetadataContainerRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<MetadataContainerRepository> logger) : IMetadataContainerRepository
{
    /// <inheritdoc />
    public async Task<MetadataContainer?> GetContainerAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.MetadataContainers
                .AsNoTracking()
                .Include(mc => mc.Contributions).ThenInclude(c => c.AlternateTexts)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Awards)
                .Include(mc => mc.Contributions).ThenInclude(c => c.ContentRatings)
                .Include(mc => mc.Contributions).ThenInclude(c => c.CustomMetadata)
                .Include(mc => mc.Contributions).ThenInclude(c => c.ExternalIds)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Genres)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Images)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Previews)
                .Include(mc => mc.Contributions).ThenInclude(c => c.References)
                .Include(mc => mc.Contributions).ThenInclude(c => c.Relationships)
                .FirstOrDefaultAsync(mc => mc.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting metadata container {ContainerId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<MetadataContribution> AddContributionAsync(
        Guid containerId,
        MetadataContribution contribution,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var containerExists = await context.MetadataContainers
                    .AnyAsync(mc => mc.Id == containerId, cancellationToken)
                    .ConfigureAwait(false);

                if (!containerExists)
                {
                    throw new InvalidOperationException($"Metadata container {containerId} not found");
                }

                contribution.MetadataContainerId = containerId;
                contribution.CreatedAt = now;
                contribution.CreatedByUserId = createdByUserId;

                context.MetadataContributions.Add(contribution);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.System,
                    Operation = "Create",
                    EntityId = contribution.Id.ToString(),
                    EntityType = nameof(MetadataContribution),
                    Detail = $"Added metadata contribution from provider {contribution.ProviderType}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return contribution;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding contribution to container {ContainerId}", containerId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveContributionAsync(
        Guid containerId,
        Guid contributionId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var contribution = await context.MetadataContributions
                    .AsNoTracking()
                    .Where(c => c.Id == contributionId && c.MetadataContainerId == containerId)
                    .Select(c => new { c.ProviderType, c.Locked })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (contribution is null)
                {
                    return false;
                }

                var deleted = await context.MetadataContributions
                    .Where(c => c.Id == contributionId && c.MetadataContainerId == containerId)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Delete",
                        EntityId = contributionId.ToString(),
                        EntityType = nameof(MetadataContribution),
                        Detail = $"Deleted metadata contribution from provider {contribution.ProviderType}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error removing contribution {ContributionId} from container {ContainerId}",
                contributionId,
                containerId);
            throw;
        }
    }
}
