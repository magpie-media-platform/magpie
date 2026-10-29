using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class MediaFileRepository(
    MagpieDbContext context,
    ILogger<MediaFileRepository> logger,
    ITransactionManager transactionManager,
    IContentRepository contentRepository,
    IContentHierarchyItemRepository hierarchyRepository,
    IEventBus eventBus) : IMediaFileRepository
{
    public async Task<MediaFile?> GetByPathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var pathKey = ToMediaFilePathKey(filePath);
            return await context.MediaFiles
                .AsNoTracking()
                .Include(mediaFile => mediaFile.Coverage)
                .FirstOrDefaultAsync(mediaFile => mediaFile.PathKey == pathKey, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The media file repository failed to read file path {FilePath}.", filePath);
            throw;
        }
    }

    public async Task<IEnumerable<MediaFile>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.MediaFiles
                .AsNoTracking()
                .Include(mediaFile => mediaFile.Coverage)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The media file repository failed to list media files.");
            throw;
        }
    }

    public async Task<MediaFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.MediaFiles
                .AsNoTracking()
                .Include(m => m.Coverage)
                .FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The media file repository failed to read media file {MediaFileId}.", id);
            throw;
        }
    }

    public async Task<PagedList<MediaFile>> QueryAsync(
        MediaFileQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var items = context.MediaFiles.AsNoTracking().AsQueryable();
            if (query.ContentItemIds is { } contentItemIds)
            {
                items = items.Where(item => contentItemIds.Contains(item.ContentItemId));
            }

            if (query.ContentHierarchyItemId is { } contentHierarchyItemId)
            {
                items = from mediaFile in items
                        join contentItem in context.ContentItems.AsNoTracking()
                            on mediaFile.ContentItemId equals contentItem.Id
                        where contentItem.ContentHierarchyItemId == contentHierarchyItemId
                        select mediaFile;
            }

            if (query.FilePaths is { } filePaths)
            {
                var pathKeys = filePaths.Select(ToMediaFilePathKey).ToList();
                items = items.Where(item => pathKeys.Contains(item.PathKey));
            }

            if (query.FilePathPrefix is { } filePathPrefix)
            {
                var prefixKey = ToMediaFilePathKey(filePathPrefix);
                items = items.Where(item => item.PathKey.StartsWith(prefixKey));
            }

            if (query.MediaInfoStaleBefore is { } staleCutoff)
            {
                items = items.Where(
                    item => item.MediaInfo == null || (item.ModifiedAt ?? item.CreatedAt) < staleCutoff);
            }

            items = ApplySort(items, sort, query.MediaInfoStaleBefore is not null);
            var totalItems = await items.CountAsync(cancellationToken).ConfigureAwait(false);
            items = items.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            return new()
            {
                Items = await items.Include(item => item.Coverage)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false),
                TotalItems = totalItems,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The media file repository failed to query media files.");
            throw;
        }
    }

    public async Task<MediaFile> CreateAsync(
        MediaFile mediaFile,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async ct =>
            {
                var items = await RequireCoveredItemsAsync(mediaFile, ct).ConfigureAwait(false);
                var owners = await LockOwnersAsync(items, ct).ConfigureAwait(false);
                mediaFile.ContentItemId = items[0];
                mediaFile.Coverage.Clear();
                foreach (var contentItemId in items.Skip(1))
                {
                    mediaFile.Coverage.Add(new() { ContentItemId = contentItemId });
                }

                mediaFile.PathKey = ToMediaFilePathKey(mediaFile.FilePath);
                mediaFile.CreatedByUserId = createdByUserId;
                context.MediaFiles.Add(mediaFile);
                await context.SaveChangesAsync(ct).ConfigureAwait(false);
                foreach (var owner in owners)
                {
                    await contentRepository.ReconcileMediaStateAsync(owner, createdByUserId, ct)
                        .ConfigureAwait(false);
                }

                await PublishUpdatedAsync(
                    items,
                    mediaFile.MediaInfo is null
                        ? [nameof(ContentItem.MediaFiles)]
                        : [nameof(ContentItem.MediaFiles), nameof(MediaFile.MediaInfo)],
                    ct).ConfigureAwait(false);
                return mediaFile;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The media file repository failed to create media file {FilePath}.",
                mediaFile.FilePath);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(
        Guid mediaFileId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async ct =>
            {
                var current = await GetByIdAsync(mediaFileId, ct).ConfigureAwait(false);
                if (current is null)
                {
                    return false;
                }

                var coveredItems =
                    from coverage in current.Coverage
                    select coverage.ContentItemId;
                var owners = await LockOwnersAsync(
                    [current.ContentItemId, .. coveredItems],
                    ct).ConfigureAwait(false);
                var removed = await context.MediaFiles.Where(file => file.Id == mediaFileId &&
                    file.ContentItemId == current.ContentItemId).ExecuteDeleteAsync(ct).ConfigureAwait(false) > 0;
                foreach (var owner in owners)
                {
                    await contentRepository.ReconcileMediaStateAsync(owner, deletedByUserId, ct)
                        .ConfigureAwait(false);
                }

                if (removed)
                {
                    await PublishUpdatedAsync(
                        [current.ContentItemId, .. current.Coverage.Select(coverage => coverage.ContentItemId)],
                        [nameof(ContentItem.MediaFiles)],
                        ct).ConfigureAwait(false);
                }

                return removed;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The media file repository failed to delete media file {MediaFileId}.", mediaFileId);
            throw;
        }
    }

    private static string ToMediaFilePathKey(string filePath) =>
        OperatingSystem.IsWindows() ? filePath.ToUpperInvariant() : filePath;

    public async Task<bool> UpdateAsync(
        MediaFile mediaFile,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async ct =>
            {
                var current = await GetByIdAsync(mediaFile.Id, ct).ConfigureAwait(false);
                if (current is null)
                {
                    return false;
                }

                var mediaInfoChanged = !string.Equals(
                    JsonSerializer.Serialize(current.MediaInfo),
                    JsonSerializer.Serialize(mediaFile.MediaInfo),
                    StringComparison.Ordinal);

                var items = await RequireCoveredItemsAsync(mediaFile, ct).ConfigureAwait(false);
                var previousItems = (
                    from coverage in current.Coverage
                    select coverage.ContentItemId).Prepend(current.ContentItemId).ToList();
                var owners = await LockOwnersAsync([.. items.Union(previousItems)], ct).ConfigureAwait(false);
                var pathKey = ToMediaFilePathKey(mediaFile.FilePath);
                var updated = await context.MediaFiles
                    .Where(item => item.Id == mediaFile.Id && item.ContentItemId == current.ContentItemId)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(item => item.ContentItemId, items[0])
                            .SetProperty(item => item.FileKind, mediaFile.FileKind)
                            .SetProperty(item => item.MimeType, mediaFile.MimeType)
                            .SetProperty(item => item.FilePath, mediaFile.FilePath)
                            .SetProperty(item => item.PathKey, pathKey)
                            .SetProperty(item => item.FileSize, mediaFile.FileSize)
                            .SetProperty(item => item.MediaInfo, mediaFile.MediaInfo)
                            .SetProperty(item => item.FileModifiedAt, mediaFile.FileModifiedAt)
                            .SetProperty(item => item.ReplacedAt, mediaFile.ReplacedAt)
                            .SetProperty(item => item.DownloadId, mediaFile.DownloadId)
                            .SetProperty(item => item.ModifiedAt, mediaFile.ModifiedAt)
                            .SetProperty(item => item.ModifiedByUserId, modifiedByUserId),
                        ct)
                    .ConfigureAwait(false);
                if (updated > 0 && (current.Coverage.Count > 0 || items.Count > 1))
                {
                    await WriteCoverageAsync(mediaFile.Id, [.. items.Skip(1)], ct).ConfigureAwait(false);
                }

                foreach (var owner in owners)
                {
                    await contentRepository.ReconcileMediaStateAsync(owner, modifiedByUserId, ct)
                        .ConfigureAwait(false);
                }

                if (updated > 0)
                {
                    var filesChanged = current.ContentItemId != items[0] ||
                        !previousItems.ToHashSet().SetEquals(items) ||
                        !string.Equals(current.FilePath, mediaFile.FilePath, StringComparison.Ordinal) ||
                        current.ReplacedAt != mediaFile.ReplacedAt;
                    List<string> changedFields = [];
                    if (filesChanged)
                    {
                        changedFields.Add(nameof(ContentItem.MediaFiles));
                    }

                    if (mediaInfoChanged)
                    {
                        changedFields.Add(nameof(MediaFile.MediaInfo));
                    }

                    await PublishUpdatedAsync([.. previousItems.Union(items)], changedFields, ct)
                        .ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The media file repository failed to update media file {MediaFileId}.", mediaFile.Id);
            throw;
        }
    }

    private async Task PublishUpdatedAsync(
        IReadOnlyCollection<Guid> contentItemIds,
        IReadOnlyCollection<string> changedFields,
        CancellationToken cancellationToken)
    {
        if (changedFields.Count == 0)
        {
            return;
        }

        var items = await context.ContentItems.AsNoTracking()
            .Where(item => contentItemIds.Contains(item.Id))
            .Select(item => new { item.Id, item.ContentHierarchyItemId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var item in items)
        {
            var impact = new ContentEventImpact
            {
                HierarchyItemId = item.ContentHierarchyItemId,
                ItemId = item.Id
            };
            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    ContentEventBuilder.BuildContentHierarchyItemUpdatedEvent(impact, changedFields),
                    token),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<Guid>> RequireCoveredItemsAsync(
        MediaFile mediaFile,
        CancellationToken cancellationToken)
    {
        var ids = mediaFile.Coverage.Select(coverage => coverage.ContentItemId)
            .Prepend(mediaFile.ContentItemId)
            .Distinct()
            .ToList();
        if (ids.Count == 1)
        {
            return ids;
        }

        var count = await (
            from item in context.ContentItems.AsNoTracking()
            where ids.Contains(item.Id)
            select item)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
        if (count != ids.Count)
        {
            throw new InvalidOperationException("An item that the media file covers does not exist.");
        }

        return ids;
    }

    private async Task WriteCoverageAsync(
        Guid mediaFileId,
        IReadOnlyCollection<Guid> contentItemIds,
        CancellationToken cancellationToken)
    {
        var stored = await context.MediaFileCoverages
            .Where(coverage => coverage.MediaFileId == mediaFileId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        context.MediaFileCoverages.RemoveRange(
            stored.Where(coverage => !contentItemIds.Contains(coverage.ContentItemId)));
        context.MediaFileCoverages.AddRange(contentItemIds
            .Except(stored.Select(coverage => coverage.ContentItemId))
            .Select(contentItemId => new MediaFileCoverage
            {
                MediaFileId = mediaFileId,
                ContentItemId = contentItemId
            }));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Guid>> LockOwnersAsync(
        IReadOnlyCollection<Guid> contentIds,
        CancellationToken cancellationToken)
    {
        var owners = await context.ContentItems.AsNoTracking().Where(item => contentIds.Contains(item.Id))
            .Select(item => item.ContentHierarchyItemId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var roots = new HashSet<Guid>();
        foreach (var owner in owners)
        {
            var lineage = await hierarchyRepository.GetLineageAsync(owner, cancellationToken).ConfigureAwait(false);
            if (lineage.Count > 0)
            {
                roots.Add(lineage[^1].Id);
            }
        }

        foreach (var root in roots.Order())
        {
            await hierarchyRepository.LockAsync(root, cancellationToken).ConfigureAwait(false);
        }

        foreach (var owner in owners)
        {
            var lineage = await hierarchyRepository.GetLineageAsync(owner, cancellationToken).ConfigureAwait(false);
            if (lineage.Count > 0 && !roots.Contains(lineage[^1].Id))
            {
                throw new DbUpdateConcurrencyException("The media hierarchy moved before its files could change.");
            }
        }

        return owners;
    }

    private IQueryable<MediaFile> ApplySort(IQueryable<MediaFile> items, SortParameter? sort, bool staleFirst)
    {
        var property = context.Model.FindEntityType(typeof(MediaFile))!.GetProperties()
            .Where(candidate => !candidate.IsShadowProperty() &&
                (candidate.ClrType.IsValueType || candidate.ClrType == typeof(string) ||
                    candidate.ClrType == typeof(Uri)))
            .Select(candidate => candidate.Name)
            .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
        if (sort is null || property is null)
        {
            return staleFirst
                ? items.OrderBy(item => item.MediaInfo == null ? 0 : 1)
                    .ThenBy(item => item.CreatedAt)
                    .ThenBy(item => item.Id)
                : items.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id);
        }

        return sort.Direction == SortDirection.Ascending
            ? items.OrderBy(item => EF.Property<object>(item, property)).ThenBy(item => item.Id)
            : items.OrderByDescending(item => EF.Property<object>(item, property)).ThenBy(item => item.Id);
    }
}
