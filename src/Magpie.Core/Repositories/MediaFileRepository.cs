using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class MediaFileRepository(
    MagpieDbContext context,
    ILogger<MediaFileRepository> logger) : IMediaFileRepository
{
    public async Task<MediaFile?> GetByPathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var pathKey = GetPathKey(filePath);
            return await context.MediaFiles
                .AsNoTracking()
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
                items = items.Where(item => filePaths.Contains(item.FilePath));
            }

            if (query.MediaInfoStaleBefore is { } staleCutoff)
            {
                items = items.Where(
                    item => item.MediaInfo == null || (item.ModifiedAt ?? item.CreatedAt) < staleCutoff);
            }

            items = query.MediaInfoStaleBefore is null
                ? items.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
                : items.OrderBy(item => item.MediaInfo == null ? 0 : 1)
                    .ThenBy(item => item.CreatedAt)
                    .ThenBy(item => item.Id);
            var totalItems = await items.CountAsync(cancellationToken).ConfigureAwait(false);
            items = items.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            return new PagedList<MediaFile>
            {
                Items = await items.ToListAsync(cancellationToken).ConfigureAwait(false),
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
            mediaFile.PathKey = GetPathKey(mediaFile.FilePath);
            mediaFile.CreatedByUserId = createdByUserId;
            context.MediaFiles.Add(mediaFile);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return mediaFile;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The media file repository failed to create media file " +
                "{FilePath}.", mediaFile.FilePath);
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
            return await context.MediaFiles
                .Where(m => m.Id == mediaFileId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false) > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The media file repository failed to delete media file {MediaFileId}.", mediaFileId);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        MediaFile mediaFile,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pathKey = GetPathKey(mediaFile.FilePath);
            var updated = await context.MediaFiles
                .Where(item => item.Id == mediaFile.Id)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(item => item.ContentItemId, mediaFile.ContentItemId)
                        .SetProperty(item => item.FileKind, mediaFile.FileKind)
                        .SetProperty(item => item.MimeType, mediaFile.MimeType)
                        .SetProperty(item => item.FilePath, mediaFile.FilePath)
                        .SetProperty(item => item.PathKey, pathKey)
                        .SetProperty(item => item.FileSize, mediaFile.FileSize)
                        .SetProperty(item => item.MediaInfo, mediaFile.MediaInfo)
                        .SetProperty(item => item.ModifiedAt, mediaFile.ModifiedAt)
                        .SetProperty(item => item.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);
            return updated > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The media file repository failed to update media file {MediaFileId}.", mediaFile.Id);
            throw;
        }
    }

    private static string GetPathKey(string filePath) =>
        OperatingSystem.IsWindows() ? filePath.ToUpperInvariant() : filePath;
}
