using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Paging;
using Magpie.Models.Patch;
using Magpie.Models.Storage;
using Magpie.QueryLanguage;
using Magpie.Sdk.Content;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Content;

internal sealed class ContentService(
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IContentRepository contentRepository,
    IAuditRecordRepository auditRecordRepository,
    IRootFolderRepository rootFolderRepository,
    IQualityTierListRepository qualityTierListRepository,
    IMetadataContainerRepository metadataContainerRepository,
    IEnumerable<IContentHierarchyMetadataProvider> metadataProviders,
    IDownloadRepository downloadRepository,
    IDownloadClientRepository downloadClientRepository,
    IEnumerable<IDownloadClientIntegration> downloadClientIntegrations,
    ITransactionManager transactionManager,
    IContentTypeRegistry contentTypeRegistry,
    IContentFileService contentFileService,
    TimeProvider timeProvider,
    ILogger<ContentService> logger) : IContentService
{
    public async Task<ContentItem> GetOrCreateContentItemAsync(
        Guid contentHierarchyItemId,
        DateTime now,
        Guid? userId,
        Guid? requestedViaWishlistId = null,
        CancellationToken cancellationToken = default)
    {
        var existing = (await contentRepository.QueryAsync(
            new ContentItemQuery { ContentHierarchyItemId = contentHierarchyItemId },
            new() { PageNumber = 1, PageSize = 2 },
            null,
            cancellationToken).ConfigureAwait(false)).Items.SingleOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        if (await contentHierarchyItemRepository
            .GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false) is null)
        {
            throw new InvalidOperationException($"Content hierarchy item {contentHierarchyItemId} does not exist.");
        }

        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = contentHierarchyItemId,
            Fulfillment = FulfillmentStatus.Missing,
            RequestedViaWishlistId = requestedViaWishlistId,
            RequestedAt = requestedViaWishlistId is not null ? now : null,
            RequestedByUserId = requestedViaWishlistId is not null ? userId : null,
            CreatedAt = now,
            CreatedByUserId = userId
        };

        try
        {
            await contentRepository.CreateAsync(contentItem, userId, cancellationToken).ConfigureAwait(false);
            return contentItem;
        }
        catch (DbUpdateException)
        {
            existing = (await contentRepository.QueryAsync(
                new ContentItemQuery { ContentHierarchyItemId = contentHierarchyItemId },
                new() { PageNumber = 1, PageSize = 2 },
                null,
                cancellationToken).ConfigureAwait(false)).Items.SingleOrDefault();

            if (existing is null)
            {
                throw;
            }

            return existing;
        }
    }

    public async Task<PagedList<ContentHierarchyItem>> QueryAsync(
        ContentQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        var criteria = BuildCriteria(query);

        var page = await contentHierarchyItemRepository
            .QueryAsync(criteria, paging, sort, cancellationToken)
            .ConfigureAwait(false);

        return page;
    }

    public async Task<ContentHierarchyItem?> GetAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        var item = await contentHierarchyItemRepository
            .GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (item is null)
        {
            return null;
        }

        var effectivePath = await contentFileService
            .GetEffectivePathAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);

        return item with { Path = effectivePath };
    }

    public async Task<PagedList<ContentHierarchyItem>?> GetChildrenAsync(
        Guid contentHierarchyItemId,
        PagingParameter paging,
        CancellationToken cancellationToken = default)
    {
        var criteria = new ContentHierarchyItemCriteria
        {
            ParentId = new CriteriaSet<PropertyCriterion<Guid>>
            {
                And = [EqualsCriterion(contentHierarchyItemId)],
            },
        };

        var page = await contentHierarchyItemRepository.QueryAsync(criteria, paging, sort: null, cancellationToken)
            .ConfigureAwait(false);

        if (page.TotalItems == 0)
        {
            var parent = await contentHierarchyItemRepository
                .QueryAsync(
                    new() { Ids = [contentHierarchyItemId] },
                    new() { PageNumber = 1, PageSize = 1 },
                    sort: null,
                    cancellationToken)
                .ConfigureAwait(false);

            if (parent.TotalItems == 0)
            {
                return null;
            }
        }

        return page;
    }

    public async Task<bool> PatchAsync(
        Guid contentHierarchyItemId,
        ContentHierarchyItemPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (patch.Slug is not null)
        {
            throw new ArgumentException("Content slug changes are not supported.", nameof(patch));
        }

        await ValidateReferencesAsync(patch, cancellationToken).ConfigureAwait(false);

        if (patch.Path is not null)
        {
            return await contentFileService.ChangePathAsync(
                contentHierarchyItemId,
                patch,
                actingUserId,
                cancellationToken).ConfigureAwait(false);
        }

        return await contentHierarchyItemRepository
            .PatchAsync(patch with { Id = contentHierarchyItemId }, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(
        Guid contentHierarchyItemId,
        bool deleteFromLibrary,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var item = await contentHierarchyItemRepository
            .GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (item is null)
        {
            return false;
        }

        if (!deleteFromLibrary)
        {
            var removedIds = await contentHierarchyItemRepository
                .DeleteSubtreeAsync(contentHierarchyItemId, actingUserId, cancellationToken)
                .ConfigureAwait(false);
            return removedIds.Count > 0;
        }

        var subtree = await ListSubtreeAsync(item, cancellationToken).ConfigureAwait(false);
        var clientResidentDownloads = await ListClientResidentDownloadsAsync(subtree, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var deletedIds = await contentHierarchyItemRepository
            .DeleteSubtreeAsync(contentHierarchyItemId, actingUserId, CancellationToken.None)
            .ConfigureAwait(false);
        if (deletedIds.Count == 0)
        {
            return false;
        }

        var removed = new HashSet<Guid>(deletedIds);

        await StopClientResidentDownloadsAsync(clientResidentDownloads, removed, CancellationToken.None)
            .ConfigureAwait(false);
        await contentFileService
            .DeleteFilesAsync([.. subtree.Where(entry => removed.Contains(entry.Id))], CancellationToken.None)
            .ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public Task<RenameTokenReference> GetRenameTokenReferenceAsync(
        RenameTokenRequest request,
        CancellationToken cancellationToken = default) =>
        contentFileService.GetRenameTokenReferenceAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<FileRenamePreview> PreviewRenameMaskAsync(
        Guid contentHierarchyItemId,
        FileRenameSelection selection,
        CancellationToken cancellationToken = default) =>
        contentFileService.PreviewRenameMaskAsync(contentHierarchyItemId, selection, cancellationToken);

    /// <inheritdoc />
    public Task<FileRenamePreview> ExecuteRenameMaskAsync(
        Guid contentHierarchyItemId,
        FileRenamePreview preview,
        Guid? userId,
        CancellationToken cancellationToken = default) =>
        contentFileService.ExecuteRenameMaskAsync(contentHierarchyItemId, preview, userId, cancellationToken);

    /// <inheritdoc />
    public Task<ContentFileBrowserLocation?> GetFileBrowserLocationAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        return contentFileService.GetBrowserLocationAsync(contentHierarchyItemId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FileSystemEntry>?> BrowseFilesAsync(
        Guid contentHierarchyItemId,
        string path,
        CancellationToken cancellationToken = default)
    {
        return contentFileService.BrowseAsync(contentHierarchyItemId, path, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ContentFileAttachmentResult?> AttachFileAsync(
        Guid contentHierarchyItemId,
        string filePath,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var operation = await contentFileService
            .AttachAsync(
                contentHierarchyItemId,
                filePath,
                actingUserId,
                ct => MaterializeFulfillmentStatusAsync(contentHierarchyItemId, actingUserId, cancellationToken: ct),
                cancellationToken)
            .ConfigureAwait(false);
        return operation.Result;
    }

    /// <inheritdoc />
    public Task<MediaFile?> ReplaceFileAsync(
        Guid contentHierarchyItemId,
        Guid mediaFileId,
        string stagedPath,
        string outputFileName,
        MediaInfo mediaInfo,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return contentFileService.ReplaceAsync(
            contentHierarchyItemId,
            mediaFileId,
            stagedPath,
            outputFileName,
            mediaInfo,
            actingUserId,
            ct => MaterializeFulfillmentStatusAsync(contentHierarchyItemId, actingUserId, cancellationToken: ct),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UnlinkFileAsync(
        Guid contentHierarchyItemId,
        Guid mediaFileId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var operation = await contentFileService
                    .UnlinkAsync(contentHierarchyItemId, mediaFileId, actingUserId, ct)
                    .ConfigureAwait(false);
                if (operation.FulfillmentChanged)
                {
                    await MaterializeFulfillmentStatusAsync(
                        contentHierarchyItemId,
                        actingUserId,
                        cancellationToken: ct).ConfigureAwait(false);
                }

                return operation.Removed;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> ReassignFileAsync(
        Guid contentHierarchyItemId,
        Guid mediaFileId,
        ReassignContentFileRequest request,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var reassigned = await contentFileService.ReassignAsync(
                    contentHierarchyItemId,
                    mediaFileId,
                    request,
                    actingUserId,
                    ct).ConfigureAwait(false);
                if (reassigned)
                {
                    await MaterializeFulfillmentStatusAsync(
                        contentHierarchyItemId,
                        actingUserId,
                        cancellationToken: ct).ConfigureAwait(false);
                    await MaterializeFulfillmentStatusAsync(
                        request.TargetContentHierarchyItemId,
                        actingUserId,
                        cancellationToken: ct).ConfigureAwait(false);
                }

                return reassigned;
            },
            cancellationToken);
    }

    public async Task<BulkResult> BulkPatchAsync(
        BulkPatch<ContentFilterCriteria, ContentHierarchyItemPatch> patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (patch.Patch.Path is not null)
        {
            throw new ArgumentException("Bulk content path changes are not supported.", nameof(patch));
        }

        if (patch.Patch.Slug is not null)
        {
            throw new ArgumentException("Bulk content slug changes are not supported.", nameof(patch));
        }

        await ValidateReferencesAsync(patch.Patch, cancellationToken).ConfigureAwait(false);

        var translated = new BulkPatch<ContentHierarchyItemCriteria, ContentHierarchyItemPatch>
        {
            Ids = patch.Ids,
            Filter = TranslateFilter(patch.Filter),
            Patch = patch.Patch,
        };

        return await contentHierarchyItemRepository
            .BulkPatchAsync(translated, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<BulkResult> BulkDeleteAsync(
        BulkAction<ContentFilterCriteria> action,
        bool deleteFromLibrary,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var translated = new BulkAction<ContentHierarchyItemCriteria>
        {
            Ids = action.Ids,
            Filter = TranslateFilter(action.Filter),
        };

        var removableItems = (IReadOnlyList<ContentHierarchyItem>)[];
        var clientResidentDownloads = (IReadOnlyList<Download>)[];
        if (deleteFromLibrary)
        {
            removableItems = await ListSelectedSubtreesAsync(translated, cancellationToken).ConfigureAwait(false);
            clientResidentDownloads = await ListClientResidentDownloadsAsync(removableItems, cancellationToken)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var (result, deletedIds) = await contentHierarchyItemRepository
            .BulkDeleteAsync(translated, actingUserId, deleteFromLibrary ? CancellationToken.None : cancellationToken)
            .ConfigureAwait(false);

        if (deleteFromLibrary && deletedIds.Count > 0)
        {
            var removed = new HashSet<Guid>(deletedIds);
            await StopClientResidentDownloadsAsync(clientResidentDownloads, removed, CancellationToken.None)
                .ConfigureAwait(false);
            await contentFileService
                .DeleteFilesAsync([.. removableItems.Where(item => removed.Contains(item.Id))], CancellationToken.None)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContentDeleteFile>> PreviewBulkDeleteAsync(
        BulkAction<ContentFilterCriteria> action,
        bool deleteFromLibrary,
        CancellationToken cancellationToken = default)
    {
        if (!deleteFromLibrary)
        {
            return [];
        }

        var translated = new BulkAction<ContentHierarchyItemCriteria>
        {
            Ids = action.Ids,
            Filter = TranslateFilter(action.Filter),
        };
        var items = await ListSelectedSubtreesAsync(translated, cancellationToken).ConfigureAwait(false);

        return
        [
            .. ListFilesRemovedWith(items)
                .OrderBy(file => file.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(file => new ContentDeleteFile { Path = file.FilePath, Size = file.FileSize })
        ];
    }

    public Task<ContentStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        return contentHierarchyItemRepository.GetStatsAsync(cancellationToken);
    }

    public async Task<ContentHierarchyItem?> HydrateFromExternalIdAsync(
        string providerType,
        string externalIdKey,
        string externalId,
        string contentType,
        string? levelSlug,
        Guid? rootFolderId,
        bool? monitoring,
        Guid? qualityTierListId,
        Guid? requestedViaWishlistId,
        IReadOnlyCollection<string>? tags,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var result = await HydrateCoreAsync(
            providerType,
            externalIdKey,
            externalId,
            contentType,
            levelSlug,
            rootFolderId,
            monitoring,
            qualityTierListId,
            requestedViaWishlistId,
            tags,
            actingUserId,
            correlationId: null,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return result is null
            ? null
            : await contentHierarchyItemRepository.GetByIdAsync(result.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(ContentHierarchyItem Root, IReadOnlyCollection<Guid> CreatedHierarchyItemIds)?>
        HydrateWithDescendantsFromExternalIdAsync(
        string providerType,
        string externalIdKey,
        string externalId,
        string contentType,
        string? levelSlug,
        Guid? rootFolderId,
        bool? monitoring,
        Guid? qualityTierListId,
        Guid? requestedViaWishlistId,
        IReadOnlyCollection<string>? tags,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ValidateHydrationRequest(providerType, externalIdKey, externalId, contentType);

        var rootLevelSlug = contentTypeRegistry.GetRootLevelSlug(contentType, levelSlug);
        var createdHierarchyItemIds = new HashSet<Guid>();
        var hasChildLevels = contentTypeRegistry.GetChildLevels(contentType, rootLevelSlug).Count > 0;
        ContentHierarchyItem? existingRoot = null;
        if (hasChildLevels)
        {
            existingRoot = await FindHierarchyItemByExternalIdAsync(
                contentType,
                rootLevelSlug,
                [(externalIdKey, externalId)],
                cancellationToken).ConfigureAwait(false);
            if (existingRoot is not null &&
                await HasChildrenAsync(existingRoot.Id, cancellationToken).ConfigureAwait(false))
            {
                return (existingRoot, createdHierarchyItemIds);
            }
        }

        var rootId = await HydrateCoreAsync(
            providerType,
            externalIdKey,
            externalId,
            contentType,
            levelSlug,
            rootFolderId,
            monitoring,
            qualityTierListId,
            requestedViaWishlistId,
            tags,
            actingUserId,
            correlationId: null,
            createdHierarchyItemIds,
            materializeDeclaredChildren: hasChildLevels,
            cancellationToken).ConfigureAwait(false);
        if (rootId is null)
        {
            return existingRoot is null ? null : (existingRoot, createdHierarchyItemIds);
        }

        var hydratedRoot = await contentHierarchyItemRepository.GetByIdAsync(rootId.Value, cancellationToken)
            .ConfigureAwait(false);
        return hydratedRoot is null ? null : (hydratedRoot, createdHierarchyItemIds);
    }

    public async Task<ContentHierarchyItem?> MaterializeFulfillmentStatusAsync(
        Guid changedContentHierarchyItemId,
        Guid? actingUserId,
        bool includeSelf = false,
        CancellationToken cancellationToken = default)
    {
        await contentRepository.ReconcileMediaStateAsync(
            changedContentHierarchyItemId,
            actingUserId,
            cancellationToken).ConfigureAwait(false);
        return await GetAsync(changedContentHierarchyItemId, cancellationToken).ConfigureAwait(false);
    }

    private static MonitoringMode? ToMonitoringMode(bool? monitoring) => monitoring switch
    {
        true => MonitoringMode.Monitored,
        false => MonitoringMode.Unmonitored,
        null => null
    };

    private static IEnumerable<MediaFile> ListFilesRemovedWith(IReadOnlyCollection<ContentHierarchyItem> items)
    {
        var removedContentItemIds = items
            .Select(item => item.ContentItem?.Id)
            .OfType<Guid>()
            .ToHashSet();

        return items
            .SelectMany(item => item.ContentItem?.MediaFiles ?? [])
            .Where(file => file.Coverage.All(coverage => removedContentItemIds.Contains(coverage.ContentItemId)));
    }

    private ContentHierarchyItemCriteria BuildCriteria(ContentQuery query)
    {
        var baseCriteria = TranslateFilter(query.Criteria) ?? new();

        if (query.RootFolderId is { } rootFolderId &&
            baseCriteria.EffectiveRootFolderId is { } filteredRootFolderId &&
            rootFolderId != filteredRootFolderId)
        {
            throw new ArgumentException("The query and filter specify different root folders.", nameof(query));
        }

        return baseCriteria with
        {
            ContentItemId = query.ContentItemId ?? baseCriteria.ContentItemId,
            ParentId = Combine(
                baseCriteria.ParentId,
                query.ParentId is { } parentId ? EqualsCriterion(parentId) : null),
            EffectiveRootFolderId = query.RootFolderId ?? baseCriteria.EffectiveRootFolderId,
            ContentType = Combine(
                baseCriteria.ContentType,
                string.IsNullOrWhiteSpace(query.ContentType) ? null : EqualsCriterion(query.ContentType)),
            Level = Combine(
                baseCriteria.Level,
                string.IsNullOrWhiteSpace(query.Level) ? null : EqualsCriterion(query.Level)),
            Name = Combine(
                baseCriteria.Name,
                string.IsNullOrWhiteSpace(query.Search) ? null : ContainsCriterion(query.Search)),
            EffectiveMonitoring = query.Monitoring ?? baseCriteria.EffectiveMonitoring,
            Fulfillment = Combine(
                baseCriteria.Fulfillment,
                query.Fulfillment is { } fulfillment ? EqualsCriterion(fulfillment) : null),
            Tags = Combine(
                baseCriteria.Tags,
                string.IsNullOrWhiteSpace(query.Tag) ? null : EqualsCriterion(query.Tag)),
            RootLevelOnly = query.TopLevelOnly == true ? true : baseCriteria.RootLevelOnly,
        };
    }

    private ContentHierarchyItemCriteria? TranslateFilter(ContentFilterCriteria? filter)
    {
        if (filter is null)
        {
            return null;
        }

        if (filter.MediaFiles.And.Any())
        {
            throw new ArgumentException(FilterQueryExtensions.LegacyMediaFilesMessage, nameof(filter));
        }

        var criteria = filter.ContentHierarchyItem.And.ToList();
        if (criteria.Count != 1)
        {
            throw new ArgumentException(
                "The content filter does not hold exactly one hierarchy item criteria set.",
                nameof(filter));
        }

        return criteria[0];
    }

    private CriteriaSet<PropertyCriterion<T>> Combine<T>(
        CriteriaSet<PropertyCriterion<T>> existing,
        PropertyCriterion<T>? addition)
    {
        return addition is null
            ? existing
            : new CriteriaSet<PropertyCriterion<T>> { And = [.. existing.And, addition] };
    }

    private PropertyCriterion<T> EqualsCriterion<T>(T value)
    {
        return new PropertyCriterion<T> { Operator = CriteriaValueOperator.Equals, Value = value };
    }

    private PropertyCriterion<string> ContainsCriterion(string value)
    {
        return new PropertyCriterion<string> { Operator = CriteriaValueOperator.ContainsString, Value = value };
    }

    private bool SupportsContentType(IEnumerable<string>? supportedContentTypes, string contentType) =>
        supportedContentTypes is not null &&
        supportedContentTypes.Any(supported => supported == "*" || supported == contentType);

    private CriteriaSet<PropertyCriterion<string>> SlugCriterion(string value) =>
        new() { And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = value }] };

    private void ValidateHydrationRequest(
        string providerType,
        string externalIdKey,
        string externalId,
        string contentType)
    {
        if (string.IsNullOrWhiteSpace(providerType) ||
            string.IsNullOrWhiteSpace(externalIdKey) ||
            string.IsNullOrWhiteSpace(externalId) ||
            string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException(
                "Provider type, external ID source, external ID, and content type are all required.");
        }
    }

    private async Task ValidateReferencesAsync(ContentHierarchyItemPatch patch, CancellationToken cancellationToken)
    {
        if (patch.RootFolderId?.Value is { } rootFolderId &&
            await rootFolderRepository.GetByIdAsync(rootFolderId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new MagpieException($"Root folder {rootFolderId} does not exist.");
        }

        if (patch.QualityTierListId?.Value is { } qualityTierListId &&
            await qualityTierListRepository.GetByIdAsync(qualityTierListId, cancellationToken)
                .ConfigureAwait(false) is null)
        {
            throw new MagpieException($"Quality tier list {qualityTierListId} does not exist.");
        }
    }

    private void AppendNormalised(StringBuilder builder, string value)
    {
        var lastWasDash = builder.Length == 0 || builder[builder.Length - 1] == '-';
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }
    }

    private bool IsSlugOfSource(string slug, string source) =>
        slug.StartsWith(BuildSlug(source, string.Empty) + "-", StringComparison.OrdinalIgnoreCase);

    private string BuildSlug(string externalIdKey, string externalId)
    {
        var builder = new StringBuilder(externalIdKey.Length + externalId.Length + 1);
        AppendNormalised(builder, externalIdKey);
        builder.Append('-');
        AppendNormalised(builder, externalId);
        var raw = builder.ToString();
        raw = raw.Trim('-');
        return string.IsNullOrWhiteSpace(raw) ? "pending" : raw;
    }

    private async Task<IReadOnlyList<ContentHierarchyItem>> ListSelectedSubtreesAsync(
        BulkAction<ContentHierarchyItemCriteria> action,
        CancellationToken cancellationToken)
    {
        var roots = new Dictionary<Guid, ContentHierarchyItem>();
        if (action.Ids is not null)
        {
            foreach (var id in action.Ids)
            {
                var item = await contentHierarchyItemRepository.GetByIdAsync(id, cancellationToken)
                    .ConfigureAwait(false);
                if (item is not null)
                {
                    roots[item.Id] = item;
                }
            }
        }

        if (action.Filter is not null)
        {
            const int pageSize = 500;
            for (var pageNumber = 1; ; pageNumber++)
            {
                var page = await contentHierarchyItemRepository.QueryAsync(
                    action.Filter,
                    new() { PageNumber = pageNumber, PageSize = pageSize },
                    sort: null,
                    cancellationToken).ConfigureAwait(false);
                var pageItems = page.Items.ToList();
                foreach (var item in pageItems)
                {
                    roots[item.Id] = item;
                }

                if (pageNumber * pageSize >= page.TotalItems || pageItems.Count == 0)
                {
                    break;
                }
            }
        }

        var subtree = new Dictionary<Guid, ContentHierarchyItem>();
        foreach (var root in roots.Values)
        {
            foreach (var item in await ListSubtreeAsync(root, cancellationToken).ConfigureAwait(false))
            {
                subtree[item.Id] = item;
            }
        }

        return [.. subtree.Values];
    }

    private async Task<IReadOnlyList<ContentHierarchyItem>> ListSubtreeAsync(
        ContentHierarchyItem root,
        CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var items = new List<ContentHierarchyItem> { root };
        for (var pageNumber = 1; ; pageNumber++)
        {
            var page = await contentHierarchyItemRepository.QueryAsync(
                new ContentHierarchyItemCriteria { AncestorId = root.Id },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            var descendants = page.Items.ToList();
            items.AddRange(descendants);
            if (pageNumber * pageSize >= page.TotalItems || descendants.Count == 0)
            {
                return items;
            }
        }
    }

    private async Task<IReadOnlyList<Download>> ListClientResidentDownloadsAsync(
        IReadOnlyCollection<ContentHierarchyItem> items,
        CancellationToken cancellationToken)
    {
        var contentItemIds = items
            .Select(item => item.ContentItem)
            .OfType<ContentItem>()
            .Select(contentItem => contentItem.Id)
            .ToArray();
        if (contentItemIds.Length == 0)
        {
            return [];
        }

        return await downloadRepository
            .ListClientResidentByContentItemsAsync(contentItemIds, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task StopClientResidentDownloadsAsync(
        IReadOnlyList<Download> downloads,
        IReadOnlySet<Guid> deletedIds,
        CancellationToken cancellationToken)
    {
        if (downloads.Count == 0)
        {
            return;
        }

        foreach (var group in downloads
            .Where(download => download.Scope.Count > 0 &&
                download.Scope.All(scope => deletedIds.Contains(scope.ContentHierarchyItemId)))
            .GroupBy(download => download.DownloadClientId))
        {
            var downloadClient = await downloadClientRepository
                .GetByIdAsync(group.Key, cancellationToken)
                .ConfigureAwait(false);
            if (downloadClient is null)
            {
                logger.LogInformation(
                    "The content service skipped {DownloadCount} downloads because client " +
                    "{DownloadClientId} no longer exists",
                    group.Count(),
                    group.Key);
                continue;
            }

            var integration = downloadClientIntegrations.LastOrDefault(candidate => string.Equals(
                candidate.ProviderType,
                downloadClient.ProviderType,
                StringComparison.Ordinal));
            if (integration is null)
            {
                logger.LogInformation(
                    "The content service skipped {DownloadCount} downloads because no provider is " +
                    "registered for {ProviderType}",
                    group.Count(),
                    downloadClient.ProviderType);
                continue;
            }

            foreach (var download in group)
            {
                if (string.IsNullOrWhiteSpace(download.ExternalId))
                {
                    continue;
                }

                try
                {
                    await integration
                        .RemoveDownloadAsync(
                            download.ExternalId,
                            deleteFiles: true,
                            downloadClient.Settings,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "The content service failed to delete download {DownloadId} (external id " +
                        "{ExternalId}) from the client during content deletion",
                        download.Id,
                        download.ExternalId);
                }
            }
        }
    }

    private async Task<Guid?> HydrateCoreAsync(
        string providerType,
        string externalIdKey,
        string externalId,
        string contentType,
        string? levelSlug,
        Guid? rootFolderId,
        bool? monitoring,
        Guid? qualityTierListId,
        Guid? requestedViaWishlistId,
        IReadOnlyCollection<string>? tags,
        Guid? userId,
        Guid? correlationId = null,
        ISet<Guid>? createdHierarchyItemIds = null,
        bool materializeDeclaredChildren = false,
        CancellationToken cancellationToken = default)
    {
        ValidateHydrationRequest(providerType, externalIdKey, externalId, contentType);

        var rootLevelSlug = contentTypeRegistry.GetRootLevelSlug(contentType, levelSlug);
        var fetched = await GetInitialMetadataAsync(
            providerType,
            externalIdKey,
            externalId,
            contentType,
            rootLevelSlug,
            cancellationToken).ConfigureAwait(false);
        if (fetched?.Contribution is not { } contribution)
        {
            logger.LogInformation("Provider {ProviderType} returned no metadata", providerType);
            return null;
        }

        if (!contribution.ExternalIds.Any(id =>
            string.Equals(id.Source, externalIdKey, StringComparison.Ordinal) &&
            string.Equals(id.Value, externalId, StringComparison.Ordinal)))
        {
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = externalIdKey,
                Value = externalId,
                Ordinal = contribution.ExternalIds.Count
            });
        }

        RootFolder? rootFolder = null;
        if (rootFolderId is { } selectedRootFolderId)
        {
            rootFolder = await rootFolderRepository.GetByIdAsync(selectedRootFolderId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Root folder {selectedRootFolderId} does not exist.");

            if (rootFolder.ContentType != contentType)
            {
                throw new InvalidOperationException(
                    $"Root folder {selectedRootFolderId} is not configured for {contentType}.");
            }
        }

        var primaryExternalId = contribution.ExternalIds.OrderBy(id => id.Ordinal).First();
        var slug = BuildSlug(primaryExternalId.Source!, primaryExternalId.Value);
        createdHierarchyItemIds ??= new HashSet<Guid>();
        var createResult = await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var result = await EnsureExternalRootAsync(
                    providerType,
                    externalIdKey,
                    externalId,
                    contribution,
                    contentType,
                    rootLevelSlug,
                    primaryExternalId.Source!,
                    slug,
                    rootFolder?.Id,
                    monitoring,
                    qualityTierListId,
                    requestedViaWishlistId,
                    tags ?? [],
                    createContentItem: true,
                    now,
                    userId,
                    createdHierarchyItemIds,
                    ct)
                    .ConfigureAwait(false);

                if (createdHierarchyItemIds.Contains(result))
                {
                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Content,
                            Operation = "Create",
                            EntityId = result.ToString(),
                            EntityType = nameof(ContentHierarchyItem),
                            Detail = $"Created content item from external ID {externalIdKey}={externalId}.",
                            Timestamp = now,
                            UserId = userId,
                            CorrelationId = correlationId
                        },
                        ct).ConfigureAwait(false);

                    logger.LogDebug(
                        "The content service created {ItemId} from external id {Key}={Value}",
                        result,
                        externalIdKey,
                        externalId);
                }
                else
                {
                    logger.LogDebug(
                        "The content hierarchy item {ItemId} already exists for external id {Key}={Value}",
                        result,
                        externalIdKey,
                        externalId);
                }

                if (materializeDeclaredChildren &&
                    fetched.Children.Count > 0 &&
                    !await HasChildrenAsync(result, ct).ConfigureAwait(false))
                {
                    var root = await contentHierarchyItemRepository.GetByIdAsync(result, ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"Content hierarchy item {result} no longer exists.");
                    await MaterializeDeclaredChildrenAsync(
                        root,
                        fetched.Children,
                        providerType,
                        monitoring,
                        userId,
                        now,
                        createdHierarchyItemIds,
                        ct)
                        .ConfigureAwait(false);
                }

                return result;
            },
            cancellationToken).ConfigureAwait(false);

        return createResult;
    }

    private async Task<ContentHierarchyMetadataResult?> GetInitialMetadataAsync(
        string providerType,
        string externalIdKey,
        string externalId,
        string contentType,
        string levelSlug,
        CancellationToken cancellationToken)
    {
        var provider = metadataProviders.LastOrDefault(candidate =>
            candidate.ProviderType == providerType &&
            SupportsContentType(candidate.SupportedContentTypes, contentType));
        if (provider is null)
        {
            return null;
        }

        var initialContribution = new MetadataContribution
        {
            ProviderType = providerType
        };
        initialContribution.ExternalIds.Add(new ExternalId
        {
            Source = externalIdKey,
            Value = externalId,
            Ordinal = 0
        });
        return await provider.TryGetMetadataAsync(
            new MetadataFetchRequest
            {
                Item = new ContentHierarchyItem
                {
                    ContentType = contentType,
                    Level = levelSlug,
                    Slug = BuildSlug(externalIdKey, externalId),
                    Name = externalId
                },
                ExistingMetadata = [initialContribution],
                DefaultLanguagePreference = string.Empty
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ContentHierarchyItem?> FindHierarchyItemBySlugAsync(
        string contentType,
        string level,
        string slug,
        CancellationToken cancellationToken)
    {
        var page = await contentHierarchyItemRepository.QueryAsync(
            new ContentHierarchyItemCriteria
            {
                ContentType = SlugCriterion(contentType),
                Level = SlugCriterion(level),
                Slug = SlugCriterion(slug),
            },
            new() { PageNumber = 1, PageSize = 1 },
            sort: null,
            cancellationToken).ConfigureAwait(false);
        return page.Items.FirstOrDefault();
    }

    private async Task<ContentHierarchyItem?> FindHierarchyItemByExternalIdAsync(
        string contentType,
        string level,
        IEnumerable<(string Source, string Value)> identities,
        CancellationToken cancellationToken)
    {
        var criteria = identities
            .Select(identity => new ExternalIdCriteria
            {
                Source = SlugCriterion(identity.Source),
                Value = SlugCriterion(identity.Value)
            })
            .ToList();
        if (criteria.Count == 0)
        {
            return null;
        }

        var page = await contentHierarchyItemRepository.QueryAsync(
            new ContentHierarchyItemCriteria
            {
                ContentType = SlugCriterion(contentType),
                Level = SlugCriterion(level),
                Metadata = new()
                {
                    And =
                    [
                        new MetadataCriteria
                        {
                            ExternalIds = new()
                            {
                                Some = [criteria]
                            }
                        }
                    ]
                }
            },
            new() { PageNumber = 1, PageSize = 1 },
            new()
            {
                PropertyName = nameof(ContentHierarchyItem.CreatedAt),
                Direction = SortDirection.Ascending,
            },
            cancellationToken).ConfigureAwait(false);
        return page.Items.FirstOrDefault();
    }

    private async Task<Guid> EnsureExternalRootAsync(
        string providerType,
        string externalIdKey,
        string externalId,
        MetadataContribution contribution,
        string contentType,
        string rootLevelSlug,
        string slugSource,
        string slug,
        Guid? rootFolderId,
        bool? monitoring,
        Guid? qualityTierListId,
        Guid? requestedViaWishlistId,
        IReadOnlyCollection<string> tags,
        bool createContentItem,
        DateTime now,
        Guid? userId,
        ISet<Guid> createdHierarchyItemIds,
        CancellationToken cancellationToken)
    {
        var existing = await FindHierarchyItemByExternalIdAsync(
            contentType,
            rootLevelSlug,
            [(externalIdKey, externalId)],
            cancellationToken).ConfigureAwait(false);
        if (existing is not null && !string.Equals(existing.Slug, slug, StringComparison.Ordinal) &&
            IsSlugOfSource(existing.Slug, slugSource))
        {
            var slugOwner = await FindHierarchyItemBySlugAsync(
                contentType,
                rootLevelSlug,
                slug,
                cancellationToken).ConfigureAwait(false);
            if (slugOwner is not null)
            {
                existing = slugOwner;
            }
            else
            {
                existing = existing with { Slug = slug };
                if (!await contentHierarchyItemRepository.PatchAsync(
                    new ContentHierarchyItemPatch { Id = existing.Id, Slug = new() { Value = slug } },
                    userId,
                    cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        $"Failed to normalize external root content for {externalIdKey}={externalId}.");
                }
            }
        }

        existing ??= await FindHierarchyItemBySlugAsync(
            contentType,
            rootLevelSlug,
            slug,
            cancellationToken).ConfigureAwait(false);
        existing ??= await FindHierarchyItemByExternalIdAsync(
            contentType,
            rootLevelSlug,
            contribution.ExternalIds
                .Where(id => !string.IsNullOrWhiteSpace(id.Source) && !string.IsNullOrWhiteSpace(id.Value))
                .Select(id => (id.Source!, id.Value)),
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.Id;
        }

        var container = new MetadataContainer { CreatedAt = now };
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = container,
            ParentId = null,
            ContentType = contentType,
            Level = rootLevelSlug,
            Slug = slug,
            Name = slug,
            PendingMetadataRefresh = true,
            RootFolderId = rootFolderId,
            Monitoring = ToMonitoringMode(monitoring),
            QualityTierListId = qualityTierListId,
            CreatedAt = now,
            CreatedByUserId = userId,
        };
        InitializeContentPath(item);

        var created = await contentHierarchyItemRepository
            .TryCreateAsync(item, userId, cancellationToken)
            .ConfigureAwait(false);
        if (!created)
        {
            var winner = await FindHierarchyItemBySlugAsync(contentType, rootLevelSlug, slug, cancellationToken)
                .ConfigureAwait(false);
            if (winner is null)
            {
                throw new InvalidOperationException(
                    $"Failed to create external root content for {externalIdKey}={externalId}.");
            }

            return winner.Id;
        }

        contribution.Id = Guid.NewGuid();
        contribution.MetadataContainerId = container.Id;
        contribution.Ordinal = 0;
        contribution.CreatedAt = now;
        contribution.CreatedByUserId = userId;
        await metadataContainerRepository
            .AddContributionAsync(container.Id, contribution, userId, cancellationToken)
            .ConfigureAwait(false);

        if (createContentItem)
        {
            await GetOrCreateContentItemAsync(item.Id, now, userId, requestedViaWishlistId, cancellationToken)
                .ConfigureAwait(false);
        }

        var normalizedTags = tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct().ToList();
        if (normalizedTags.Count > 0)
        {
            await contentHierarchyItemRepository
                .BulkPatchAsync(
                    new BulkPatch<ContentHierarchyItemCriteria, ContentHierarchyItemPatch>
                    {
                        Ids = [item.Id],
                        Patch = new ContentHierarchyItemPatch
                        {
                            Tags = new SetPatch<string, string> { Add = normalizedTags },
                        },
                    },
                    userId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        createdHierarchyItemIds.Add(item.Id);
        return item.Id;
    }

    private void InitializeContentPath(ContentHierarchyItem item)
    {
        var level = contentTypeRegistry.GetDefinitionFor(item.ContentType)?.LevelDefinitions
            .FirstOrDefault(definition => definition.Slug == item.Level);
        var origin = item.Settings.TryGetValue("content.path.origin", out var value) ? value : null;
        if (level?.Subfolder is null ||
            !(string.Equals(origin, "Generated", StringComparison.OrdinalIgnoreCase) ||
                origin is null && item.Path is null))
        {
            return;
        }

        var segment = (string.IsNullOrWhiteSpace(item.Name) ? item.Slug : item.Name).NormalizeContentPathSegment();
        if (segment.Length == 0)
        {
            segment = item.Slug;
        }

        item.Path = segment;
        item.Settings["content.path.origin"] = "Generated";
    }

    private async Task<bool> HasChildrenAsync(Guid parentId, CancellationToken cancellationToken)
    {
        var page = await contentHierarchyItemRepository.QueryAsync(
            new ContentHierarchyItemCriteria
            {
                ParentId = new CriteriaSet<PropertyCriterion<Guid>> { And = [EqualsCriterion(parentId)] },
            },
            new() { PageNumber = 1, PageSize = 1 },
            sort: null,
            cancellationToken).ConfigureAwait(false);
        return page.TotalItems > 0;
    }

    private async Task MaterializeDeclaredChildrenAsync(
        ContentHierarchyItem parent,
        IReadOnlyList<ChildHierarchyDefinition> definitions,
        string providerType,
        bool? monitoring,
        Guid? userId,
        DateTime now,
        ISet<Guid> createdHierarchyItemIds,
        CancellationToken cancellationToken)
    {
        var childLevels = contentTypeRegistry.GetChildLevels(parent.ContentType, parent.Level)
            .Select(level => level.Slug)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(definition.Level) ||
                string.IsNullOrWhiteSpace(definition.Name) ||
                string.IsNullOrWhiteSpace(definition.Slug) ||
                !childLevels.Contains(definition.Level))
            {
                continue;
            }

            var metadataContainerId = Guid.NewGuid();
            var child = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                MetadataContainerId = metadataContainerId,
                MetadataContainer = new MetadataContainer { Id = metadataContainerId, CreatedAt = now },
                ParentId = parent.Id,
                ContentType = parent.ContentType,
                Level = definition.Level,
                Slug = definition.Slug,
                Name = definition.Name,
                Ordinal = definition.Ordinal,
                AlignmentKey = definition.AlignmentKey,
                PendingMetadataRefresh = true,
                Monitoring = ToMonitoringMode(monitoring),
                CreatedAt = now,
                CreatedByUserId = userId,
            };
            InitializeContentPath(child);

            if (!await contentHierarchyItemRepository.TryCreateAsync(child, userId, cancellationToken)
                .ConfigureAwait(false))
            {
                var page = await contentHierarchyItemRepository.QueryAsync(
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = SlugCriterion(parent.ContentType),
                        ParentId = new CriteriaSet<PropertyCriterion<Guid>>
                        {
                            And = [EqualsCriterion(parent.Id)]
                        },
                        Level = SlugCriterion(definition.Level),
                        Slug = SlugCriterion(definition.Slug)
                    },
                    new() { PageNumber = 1, PageSize = 1 },
                    sort: null,
                    cancellationToken).ConfigureAwait(false);
                child = page.Items.FirstOrDefault()
                    ?? throw new InvalidOperationException(
                        $"Failed to create hierarchy child {definition.Level}/{definition.Slug} under {parent.Id}.");
            }
            else
            {
                createdHierarchyItemIds.Add(child.Id);
                await AddInitialChildContributionAsync(child, definition, providerType, userId, now, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (definition.Children.Count > 0)
            {
                await MaterializeDeclaredChildrenAsync(
                    child,
                    definition.Children,
                    providerType,
                    monitoring,
                    userId,
                    now,
                    createdHierarchyItemIds,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task AddInitialChildContributionAsync(
        ContentHierarchyItem child,
        ChildHierarchyDefinition definition,
        string providerType,
        Guid? userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var externalIds = definition.ExternalIds
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
            .ToArray();
        var contribution = definition.InitialContribution;
        if (contribution is null && externalIds.Length == 0)
        {
            return;
        }

        contribution ??= new MetadataContribution { ProviderType = providerType };
        if (string.IsNullOrWhiteSpace(contribution.ProviderType))
        {
            contribution.ProviderType = providerType;
        }

        contribution.MetadataContainerId = child.MetadataContainerId;
        contribution.Ordinal = 0;
        contribution.CreatedAt = now;
        contribution.CreatedByUserId = userId;
        contribution.DateLastRefreshed = now;
        foreach (var (source, value) in externalIds)
        {
            if (contribution.ExternalIds.Any(externalId =>
                string.Equals(externalId.Source, source, StringComparison.Ordinal) &&
                string.Equals(externalId.Value, value, StringComparison.Ordinal)))
            {
                continue;
            }

            contribution.ExternalIds.Add(new ExternalId
            {
                Source = source,
                Value = value,
                Ordinal = contribution.ExternalIds.Count
            });
        }

        var added = await metadataContainerRepository
            .AddContributionAsync(child.MetadataContainerId, contribution, userId, cancellationToken)
            .ConfigureAwait(false);
        child.MetadataContainer!.Contributions.Add(added);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContentHierarchyItem>> PreviewImportHierarchyAsync(
        PreviewByExternalIdRequest request,
        Guid rootFolderId,
        CancellationToken cancellationToken = default)
    {
        ValidateHydrationRequest(request.ProviderType, request.ExternalIdKey, request.ExternalId, request.ContentType);
        var level = contentTypeRegistry.GetRootLevelSlug(request.ContentType, request.LevelSlug);
        var existing = await FindHierarchyItemByExternalIdAsync(
            request.ContentType,
            level,
            [(request.ExternalIdKey, request.ExternalId)],
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            var descendants = await contentHierarchyItemRepository.QueryAllAsync(
                new ContentHierarchyItemCriteria { AncestorId = existing.Id },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (descendants.Count > 0 || contentTypeRegistry.GetChildLevels(request.ContentType, level).Count == 0)
            {
                var lineage = await contentHierarchyItemRepository.GetLineageAsync(existing.Id, cancellationToken)
                    .ConfigureAwait(false);
                return [existing, .. descendants.Where(item => item.Id != existing.Id), .. lineage.Skip(1)];
            }
        }

        var fetched = await GetInitialMetadataAsync(
            request.ProviderType,
            request.ExternalIdKey,
            request.ExternalId,
            request.ContentType,
            level,
            cancellationToken).ConfigureAwait(false);
        if (fetched?.Contribution is not { } contribution)
        {
            throw new InvalidOperationException("The metadata provider returned no hierarchy for the rename preview.");
        }

        var primaryId = contribution.ExternalIds.OrderBy(id => id.Ordinal).FirstOrDefault();
        var slug = BuildSlug(primaryId?.Source ?? request.ExternalIdKey, primaryId?.Value ?? request.ExternalId);
        var root = existing ?? new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = request.ContentType,
            Level = level,
            Name = slug,
            Slug = slug,
            RootFolderId = rootFolderId,
            EffectiveRootFolderId = rootFolderId,
            PendingMetadataRefresh = true,
            MetadataContainer = new() { Contributions = { contribution } }
        };
        if (existing is null)
        {
            InitializeContentPath(root);
        }

        var nodes = new List<ContentHierarchyItem> { root };
        AddPreviewChildren(root, fetched.Children, nodes, request.ProviderType);
        if (existing is not null)
        {
            var lineage = await contentHierarchyItemRepository.GetLineageAsync(existing.Id, cancellationToken)
                .ConfigureAwait(false);
            nodes.AddRange(lineage.Skip(1));
        }

        return nodes;
    }

    private void AddPreviewChildren(
        ContentHierarchyItem parent,
        IReadOnlyList<ChildHierarchyDefinition> definitions,
        List<ContentHierarchyItem> nodes,
        string providerType)
    {
        var levels = contentTypeRegistry.GetChildLevels(parent.ContentType, parent.Level)
            .Select(level => level.Slug).ToHashSet(StringComparer.Ordinal);
        foreach (var definition in definitions.Where(definition => levels.Contains(definition.Level) &&
            !string.IsNullOrWhiteSpace(definition.Name) && !string.IsNullOrWhiteSpace(definition.Slug)))
        {
            var child = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ParentId = parent.Id,
                ContentType = parent.ContentType,
                Level = definition.Level,
                Name = definition.Name,
                Slug = definition.Slug,
                Ordinal = definition.Ordinal,
                AlignmentKey = definition.AlignmentKey,
                EffectiveRootFolderId = parent.EffectiveRootFolderId,
                PendingMetadataRefresh = true,
                MetadataContainer = new()
            };
            if (definition.InitialContribution is { } supplied)
            {
                child.MetadataContainer.Contributions.Add(supplied);
            }

            var initial = new MetadataContribution { ProviderType = providerType };
            foreach (var (source, value) in definition.ExternalIds.Where(pair =>
                !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)))
            {
                if (definition.InitialContribution?.ExternalIds.Any(id => id.Source == source && id.Value == value)
                    is not true)
                {
                    initial.ExternalIds.Add(new ExternalId
                    {
                        Source = source,
                        Value = value,
                        Ordinal = initial.ExternalIds.Count
                    });
                }
            }

            child.MetadataContainer.Contributions.Add(initial);

            InitializeContentPath(child);
            nodes.Add(child);
            AddPreviewChildren(child, definition.Children, nodes, providerType);
        }
    }
}
