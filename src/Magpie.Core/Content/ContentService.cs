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
        ArgumentNullException.ThrowIfNull(query);

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

        return item;
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
        ArgumentNullException.ThrowIfNull(patch);

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

        var subtree = await ListSubtreeAsync(item, cancellationToken).ConfigureAwait(false);
        var contentItemIds = subtree
            .Select(subtreeItem => subtreeItem.ContentItem)
            .Where(contentItem => contentItem is not null)
            .Select(contentItem => contentItem!.Id)
            .ToArray();
        await StopClientResidentDownloadsAsync(contentItemIds, cancellationToken).ConfigureAwait(false);

        if (deleteFromLibrary)
        {
            await contentFileService.DeleteFilesAsync(subtree, cancellationToken)
                .ConfigureAwait(false);
        }

        return await contentHierarchyItemRepository
            .DeleteAsync(contentHierarchyItemId, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

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
                ct => MaterializeFulfillmentStatusAsync(
                    contentHierarchyItemId,
                    actingUserId,
                    cancellationToken: ct),
                cancellationToken)
            .ConfigureAwait(false);
        return operation.Result;
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

    public async Task<BulkResult> BulkPatchAsync(
        BulkPatch<ContentFilterCriteria, ContentHierarchyItemPatch> patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        if (patch.Patch.Path is not null)
        {
            throw new ArgumentException("Bulk content path changes are not supported.", nameof(patch));
        }

        var translated = new BulkPatch<ContentHierarchyItemCriteria, ContentHierarchyItemPatch>
        {
            Ids = patch.Ids,
            Filter = TranslateFilter(contentTypeRegistry, patch.Filter),
            Patch = patch.Patch,
        };

        return await contentHierarchyItemRepository
            .BulkPatchAsync(translated, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<BulkResult> BulkDeleteAsync(
        BulkAction<ContentFilterCriteria> action,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        var translated = new BulkAction<ContentHierarchyItemCriteria>
        {
            Ids = action.Ids,
            Filter = TranslateFilter(contentTypeRegistry, action.Filter),
        };

        return await contentHierarchyItemRepository
            .BulkDeleteAsync(translated, actingUserId, cancellationToken)
            .ConfigureAwait(false);
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
            cancellationToken)
            .ConfigureAwait(false);
        return result is null
            ? null
            : await contentHierarchyItemRepository.GetByIdAsync(
                result.Value,
                cancellationToken).ConfigureAwait(false);
    }

    public async Task<ContentHierarchyItem?> MaterializeFulfillmentStatusAsync(
        Guid changedContentHierarchyItemId,
        Guid? actingUserId,
        bool includeSelf = false,
        CancellationToken cancellationToken = default)
    {
        var lineage = await contentHierarchyItemRepository
            .GetLineageAsync(changedContentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (lineage.Count == 0)
        {
            return null;
        }

        var overrides = new Dictionary<Guid, FulfillmentStatus>();
        var nodesToCompute = includeSelf ? lineage : lineage.Skip(1);
        var pending = new List<PendingRollup>();
        const int pageSize = 500;
        foreach (var node in nodesToCompute)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var children = new List<ContentHierarchyItem>();
            var pageNumber = 1;
            while (true)
            {
                var page = await contentHierarchyItemRepository.QueryAsync(
                    new ContentHierarchyItemCriteria
                    {
                        ParentId = new CriteriaSet<PropertyCriterion<Guid>>
                        {
                            And = [EqualsCriterion(node.Id)],
                        },
                    },
                    new() { PageNumber = pageNumber, PageSize = pageSize },
                    sort: null,
                    cancellationToken).ConfigureAwait(false);
                var pageItems = page.Items.ToList();
                children.AddRange(pageItems);
                if (pageItems.Count < pageSize)
                {
                    break;
                }

                pageNumber++;
            }

            var childStatuses = new List<FulfillmentStatus>(children.Count);
            foreach (var child in children)
            {
                childStatuses.Add(await ResolveStoredFulfillmentAsync(child.Id, overrides, cancellationToken)
                    .ConfigureAwait(false));
            }

            var computed = AggregateFulfillment(childStatuses);
            var current = await ResolveStoredFulfillmentAsync(node.Id, overrides, cancellationToken)
                .ConfigureAwait(false);
            overrides[node.Id] = computed;
            if (computed != current)
            {
                pending.Add(new PendingRollup(node.Id, current, computed));
            }
        }

        if (pending.Count > 0)
        {
            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var now = timeProvider.GetUtcNow().UtcDateTime;
                    foreach (var rollup in pending)
                    {
                        var contentItem = (await contentRepository.QueryAsync(
                            new ContentItemQuery { ContentHierarchyItemId = rollup.HierarchyItemId },
                            new() { PageNumber = 1, PageSize = 2 },
                            null,
                            ct).ConfigureAwait(false)).Items.SingleOrDefault() ??
                                await GetOrCreateContentItemAsync(
                                    rollup.HierarchyItemId,
                                    now,
                                    actingUserId,
                                    cancellationToken: ct).ConfigureAwait(false);

                        contentItem.Fulfillment = rollup.To;
                        contentItem.ModifiedAt = now;

                        await contentRepository.UpdateAsync(contentItem, actingUserId, ct).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "The content service materialized fulfillment for {Count} content items from hierarchy " +
                "item {ContentHierarchyItemId}",
                pending.Count,
                changedContentHierarchyItemId);
        }

        return await GetAsync(changedContentHierarchyItemId, cancellationToken).ConfigureAwait(false);
    }

    private ContentHierarchyItemCriteria BuildCriteria(ContentQuery query)
    {
        var baseCriteria = query.Criteria?.ContentHierarchyItem.And.FirstOrDefault()
            ?? new ContentHierarchyItemCriteria();

        return new ContentHierarchyItemCriteria
        {
            ParentId = Combine(
                baseCriteria.ParentId,
                query.ParentId is { } parentId ? EqualsCriterion(parentId) : null),
            RootFolderId = baseCriteria.RootFolderId,
            EffectiveRootFolderId = query.RootFolderId ?? baseCriteria.EffectiveRootFolderId,
            ContentType = Combine(
                baseCriteria.ContentType,
                string.IsNullOrWhiteSpace(query.ContentType) ? null : EqualsCriterion(query.ContentType!)),
            Level = Combine(
                baseCriteria.Level,
                string.IsNullOrWhiteSpace(query.Level) ? null : EqualsCriterion(query.Level!)),
            Slug = baseCriteria.Slug,
            Name = Combine(
                baseCriteria.Name,
                string.IsNullOrWhiteSpace(query.Search) ? null : ContainsCriterion(query.Search!)),
            Monitoring = Combine(
                baseCriteria.Monitoring,
                query.Monitoring is { } monitoring ? EqualsCriterion(monitoring) : null),
            EffectiveMonitoring = baseCriteria.EffectiveMonitoring,
            Fulfillment = Combine(
                baseCriteria.Fulfillment,
                query.Fulfillment is { } fulfillment ? EqualsCriterion(fulfillment) : null),
            ActiveAcquisitionStatus = baseCriteria.ActiveAcquisitionStatus,
            QualityStatus = baseCriteria.QualityStatus,
            Path = baseCriteria.Path,
            Tags = Combine(
                baseCriteria.Tags,
                string.IsNullOrWhiteSpace(query.Tag) ? null : EqualsCriterion(query.Tag!)),
            Metadata = baseCriteria.Metadata,
            RootLevelOnly = query.TopLevelOnly == true ? true : baseCriteria.RootLevelOnly,
            LeafOnly = baseCriteria.LeafOnly,
            PendingMetadataRefresh = baseCriteria.PendingMetadataRefresh,
            HasMetadataExternalIds = baseCriteria.HasMetadataExternalIds,
            HasSearchableTitle = baseCriteria.HasSearchableTitle,
            ExcludeIds = baseCriteria.ExcludeIds,
            AncestorId = baseCriteria.AncestorId,
        };
    }

    private ContentHierarchyItemCriteria? TranslateFilter(
        IContentTypeRegistry registry,
        ContentFilterCriteria? filter)
    {
        if (filter is null)
        {
            return null;
        }

        return filter.ContentHierarchyItem.And.FirstOrDefault() ?? new ContentHierarchyItemCriteria();
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

    private FulfillmentStatus AggregateFulfillment(IReadOnlyList<FulfillmentStatus> childStatuses)
    {
        if (childStatuses.Count == 0)
        {
            return FulfillmentStatus.Fulfilled;
        }

        var fullyCovered = childStatuses.Count(status =>
            status is not FulfillmentStatus.Missing and not FulfillmentStatus.Partial);
        if (fullyCovered == childStatuses.Count)
        {
            return childStatuses.Contains(FulfillmentStatus.Upgradable)
                ? FulfillmentStatus.Upgradable
                : FulfillmentStatus.Fulfilled;
        }

        return childStatuses.Any(status => status != FulfillmentStatus.Missing)
            ? FulfillmentStatus.Partial
            : FulfillmentStatus.Missing;
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
            if (descendants.Count < pageSize)
            {
                return items;
            }
        }
    }

    private async Task StopClientResidentDownloadsAsync(
        IReadOnlyList<Guid> contentItemIds,
        CancellationToken cancellationToken)
    {
        if (contentItemIds.Count == 0)
        {
            return;
        }

        var downloads = await downloadRepository
            .ListClientResidentByContentItemsAsync(contentItemIds, cancellationToken)
            .ConfigureAwait(false);
        if (downloads.Count == 0)
        {
            return;
        }

        foreach (var group in downloads.GroupBy(download => download.DownloadClientId))
        {
            var downloadClient = await downloadClientRepository
                .GetByIdAsync(group.Key, cancellationToken)
                .ConfigureAwait(false);
            if (downloadClient is null)
            {
                logger.LogInformation(
                    "The content service did not delete {DownloadCount} downloads from the client. " +
                    "Client {DownloadClientId} does not exist any more",
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
                    "The content service did not delete {DownloadCount} downloads from the client. " +
                    "No provider is registered for {ProviderType}",
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
                            download.ExternalId, deleteFiles: true, downloadClient.Settings, cancellationToken)
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
                        "The content service failed to delete download {DownloadId} (external id {ExternalId}) " +
                        "from the client during content deletion. The library deletion will continue",
                        download.Id,
                        download.ExternalId);
                }
            }
        }
    }

    private async Task<FulfillmentStatus> ResolveStoredFulfillmentAsync(
        Guid hierarchyItemId,
        IReadOnlyDictionary<Guid, FulfillmentStatus> overrides,
        CancellationToken cancellationToken)
    {
        if (overrides.TryGetValue(hierarchyItemId, out var overridden))
        {
            return overridden;
        }

        var contentItem = (await contentRepository.QueryAsync(
            new ContentItemQuery { ContentHierarchyItemId = hierarchyItemId },
            new() { PageNumber = 1, PageSize = 2 },
            null,
            cancellationToken).ConfigureAwait(false)).Items.SingleOrDefault();
        return contentItem?.Fulfillment ?? FulfillmentStatus.Missing;
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
        CancellationToken cancellationToken = default)
    {
        ValidateHydrationRequest(providerType, externalIdKey, externalId, contentType);

        var rootLevelSlug = contentTypeRegistry.GetRootLevelSlug(contentType, levelSlug);
        var contribution = await GetInitialContributionAsync(
            providerType,
            externalIdKey,
            externalId,
            contentType,
            rootLevelSlug,
            cancellationToken).ConfigureAwait(false);
        if (contribution is null)
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
                ?? throw new InvalidOperationException(
                    $"Root folder {selectedRootFolderId} does not exist. Content creation stopped.");

            if (rootFolder.ContentType != contentType)
            {
                throw new InvalidOperationException(
                    $"Root folder {selectedRootFolderId} is not configured for {contentType}, cannot create content.");
            }
        }

        var primaryExternalId = contribution.ExternalIds.OrderBy(id => id.Ordinal).First();
        var slug = BuildSlug(primaryExternalId.Source!, primaryExternalId.Value);
        var createdHierarchyItemIds = new HashSet<Guid>();
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

                if (!createdHierarchyItemIds.Contains(result))
                {
                    logger.LogInformation(
                        "The content service will return the existing content hierarchy item {ItemId} for " +
                        "external id {Key}={Value}",
                        result,
                        externalIdKey,
                        externalId);
                    return result;
                }

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

                logger.LogInformation(
                    "The content service created {ItemId} from external id {Key}={Value} with provider " +
                    "{Provider} and content type {ContentType}",
                    result,
                    externalIdKey,
                    externalId,
                    providerType,
                    contentType);

                return result;
            },
            cancellationToken).ConfigureAwait(false);

        return createResult;
    }

    private async Task<MetadataContribution?> GetInitialContributionAsync(
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
        var result = await provider.TryGetMetadataAsync(
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
        return result?.Contribution;
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
        string source,
        string value,
        CancellationToken cancellationToken)
    {
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
                                Some =
                                [
                                    [
                                        new ExternalIdCriteria
                                        {
                                            Source = SlugCriterion(source),
                                            Value = SlugCriterion(value)
                                        }
                                    ]
                                ]
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
            externalIdKey,
            externalId,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null && !string.Equals(existing.Slug, slug, StringComparison.Ordinal))
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
                if (!await contentHierarchyItemRepository.UpdateAsync(existing, userId, cancellationToken)
                    .ConfigureAwait(false))
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
            Monitoring = monitoring,
            QualityTierListId = qualityTierListId,
            CreatedAt = now,
            CreatedByUserId = userId,
        };
        InitializeContentPath(item, contentTypeRegistry);

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

    private void InitializeContentPath(ContentHierarchyItem item, IContentTypeRegistry registry, int? year = null)
    {
        var level = registry.GetDefinitionFor(item.ContentType)?.LevelDefinitions
            .FirstOrDefault(definition => definition.Slug == item.Level);
        var origin = item.Settings.TryGetValue("content.path.origin", out var value) ? value : null;
        if (level?.Subfolder is null ||
            !(string.Equals(origin, "Generated", StringComparison.OrdinalIgnoreCase) ||
                origin is null && item.Path is null))
        {
            return;
        }

        var source = string.IsNullOrWhiteSpace(item.Name) ? item.Slug : item.Name;
        var characters = source.Trim().Select(character =>
            character is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(character)
                ? ' '
                : character).ToArray();
        var segment = string.Join(' ', new string(characters)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        if (segment.Length == 0)
        {
            segment = item.Slug;
        }

        item.Path = year is null ? segment : $"{segment} ({year.Value})";
        item.Settings["content.path.origin"] = "Generated";
    }

    private readonly record struct PendingRollup(Guid HierarchyItemId, FulfillmentStatus From, FulfillmentStatus To);
}
