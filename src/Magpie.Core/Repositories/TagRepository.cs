using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

/// <summary>
/// Read-only repository for querying tags.
/// </summary>
public sealed class TagRepository(MagpieDbContext context, ILogger<TagRepository> logger) : ITagRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<Tag>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.Tags
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing tags");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Tag>> GetByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Tags
                .AsNoTracking()
                .Where(t => t.Name.StartsWith(prefix))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting tags by prefix {Prefix}", prefix);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Tag>> GetTagsByEntityAsync(Guid entityId, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Tags
                .AsNoTracking()
                .Where(t =>
                    t.DownloadClients.Any(e => e.Id == entityId) ||
                    t.IndexerConnections.Any(e => e.Id == entityId) ||
                    t.MediaServers.Any(e => e.Id == entityId) ||
                    t.MetadataContainers.Any(e => e.Id == entityId) ||
                    t.SubjectFilters.Any(e => e.Id == entityId) ||
                    t.ContentFilters.Any(e => e.Id == entityId) ||
                    t.QualityTierLists.Any(e => e.Id == entityId) ||
                    t.Releases.Any(e => e.Id == entityId) ||
                    t.Wishlists.Any(e => e.Id == entityId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting tags by entity {EntityId}", entityId);
            throw;
        }
    }
}
