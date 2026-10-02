using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class TagRepository(MagpieDbContext context, ILogger<TagRepository> logger) : ITagRepository
{
    private IQueryable<Tag> ApplySort(IQueryable<Tag> tags, SortParameter? sort)
    {
        var property = context.Model.FindEntityType(typeof(Tag))!.GetProperties()
            .Where(candidate => !candidate.IsShadowProperty() &&
                (candidate.ClrType.IsValueType || candidate.ClrType == typeof(string) ||
                    candidate.ClrType == typeof(Uri)))
            .Select(candidate => candidate.Name)
            .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
        if (sort is null || property is null)
        {
            return tags.OrderBy(tag => tag.Name);
        }

        return sort.Direction == SortDirection.Ascending
            ? tags.OrderBy(tag => EF.Property<object>(tag, property)).ThenBy(tag => tag.Name)
            : tags.OrderByDescending(tag => EF.Property<object>(tag, property)).ThenBy(tag => tag.Name);
    }

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
            logger.LogError(ex, "The tag repository failed to list tags.");
            throw;
        }
    }

    public async Task<Tag?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.Tags
                .AsNoTracking()
                .FirstOrDefaultAsync(tag => tag.Name == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The tag repository failed to get tag {TagId}.", id);
            throw;
        }
    }

    public async Task<PagedList<Tag>> QueryAsync(
        TagQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tags = context.Tags.AsNoTracking().AsQueryable();
            if (query.Prefix is { } prefix)
            {
                tags = tags.Where(tag => tag.Name.StartsWith(prefix));
            }

            if (query.EntityId is { } entityId)
            {
                var contentContainerIds = context.ContentHierarchyItems
                    .Where(item => item.Id == entityId)
                    .Select(item => item.MetadataContainerId);
                var subjectContainerIds = context.Subjects
                    .Where(subject => subject.Id == entityId)
                    .Select(subject => subject.MetadataContainerId);
                tags = tags.Where(tag =>
                    tag.DownloadClients.Any(entity => entity.Id == entityId) ||
                    tag.IndexerConnections.Any(entity => entity.Id == entityId) ||
                    tag.MediaServers.Any(entity => entity.Id == entityId) ||
                    tag.MetadataContainers.Any(entity => entity.Id == entityId) ||
                    tag.MetadataContainers.Any(entity => contentContainerIds.Contains(entity.Id)) ||
                    tag.MetadataContainers.Any(entity => subjectContainerIds.Contains(entity.Id)) ||
                    tag.SubjectFilters.Any(entity => entity.Id == entityId) ||
                    tag.ContentFilters.Any(entity => entity.Id == entityId) ||
                    tag.QualityTierLists.Any(entity => entity.Id == entityId) ||
                    tag.Releases.Any(entity => entity.Id == entityId) ||
                    tag.Wishlists.Any(entity => entity.Id == entityId));
            }

            tags = ApplySort(tags, sort);
            var totalItems = await tags.CountAsync(cancellationToken).ConfigureAwait(false);
            tags = tags.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            return new()
            {
                Items = await tags.ToListAsync(cancellationToken).ConfigureAwait(false),
                TotalItems = totalItems,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The tag repository failed to query tags.");
            throw;
        }
    }
}
