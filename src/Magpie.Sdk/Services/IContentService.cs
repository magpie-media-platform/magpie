using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Paging;
using Magpie.Models.Storage;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for content hierarchy items.
/// </summary>
public interface IContentService
{
    /// <summary>
    /// Corrects a file association within a content hierarchy.
    /// </summary>
    /// <param name="contentHierarchyItemId">The current hierarchy owner</param>
    /// <param name="mediaFileId">The file to correct</param>
    /// <param name="request">The selected destination and expected file path</param>
    /// <param name="actingUserId">The user who corrects the association</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> ReassignFileAsync(
        Guid contentHierarchyItemId,
        Guid mediaFileId,
        ReassignContentFileRequest request,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an import hierarchy for a read-only preview without creating library items.
    /// </summary>
    /// <param name="request">The selected external metadata identity</param>
    /// <param name="rootFolderId">The root folder selected for import</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<ContentHierarchyItem>> PreviewImportHierarchyAsync(
        PreviewByExternalIdRequest request,
        Guid rootFolderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the registered rename tokens and previews a rename mask with example or content values.
    /// </summary>
    /// <param name="request">The content type or selected content and the mask to preview</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<RenameTokenReference> GetRenameTokenReferenceAsync(
        RenameTokenRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Previews explicit masks for selected files or descendant files without changing storage.
    /// </summary>
    /// <param name="contentHierarchyItemId">The selected content parent or owner</param>
    /// <param name="selection">The file selection</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<FileRenamePreview> PreviewRenameMaskAsync(
        Guid contentHierarchyItemId,
        FileRenameSelection selection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the valid entries of a previously displayed explicit rename preview.
    /// </summary>
    /// <param name="contentHierarchyItemId">The selected content parent or owner</param>
    /// <param name="preview">The displayed preview</param>
    /// <param name="userId">The user that requests the move</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<FileRenamePreview> ExecuteRenameMaskAsync(
        Guid contentHierarchyItemId,
        FileRenamePreview preview,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries the content hierarchy items in the library.
    /// </summary>
    /// <param name="query">The filter criteria for the content items</param>
    /// <param name="paging">The page number and page size</param>
    /// <param name="sort">The sort field and direction, or <see langword="null"/> for the default order</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<PagedList<ContentHierarchyItem>> QueryAsync(
        ContentQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A content hierarchy item by id.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the item to return</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ContentHierarchyItem?> GetAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the content item for a hierarchy item, creating one when absent.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the hierarchy item</param>
    /// <param name="now">The UTC time to record</param>
    /// <param name="userId">The id of the user that starts the operation</param>
    /// <param name="requestedViaWishlistId">The id of the wishlist that requested the content</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ContentItem> GetOrCreateContentItemAsync(
        Guid contentHierarchyItemId,
        DateTime now,
        Guid? userId,
        Guid? requestedViaWishlistId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A page of the children of a content hierarchy item, or <see langword="null"/> when the parent is absent.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the parent item</param>
    /// <param name="paging">The page number and page size</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<PagedList<ContentHierarchyItem>?> GetChildrenAsync(
        Guid contentHierarchyItemId,
        PagingParameter paging,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the item to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchAsync(
        Guid contentHierarchyItemId,
        ContentHierarchyItemPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a content hierarchy item and its descendants.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the item at the top of the subtree</param>
    /// <param name="deleteFromLibrary">Whether the delete also removes the files from disk</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteAsync(
        Guid contentHierarchyItemId,
        bool deleteFromLibrary,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the root and initial path for the file browser of a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ContentFileBrowserLocation?> GetFileBrowserLocationAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a directory in the configured root of a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="path">The directory to list</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<FileSystemEntry>?> BrowseFilesAsync(
        Guid contentHierarchyItemId,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches an existing host file to a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="filePath">The full path of the host file</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ContentFileAttachmentResult?> AttachFileAsync(
        Guid contentHierarchyItemId,
        string filePath,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a media file with a staged output file.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="mediaFileId">The id of the media file to replace</param>
    /// <param name="stagedPath">
    /// The full path of the staged output file, named <c>&lt;outputFileName&gt;.&lt;lineage&gt;.partial</c>
    /// </param>
    /// <param name="outputFileName">The file name of the output</param>
    /// <param name="mediaInfo">The media information of the output</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The media file of the output, or <see langword="null"/> when the content item does not exist</returns>
    Task<MediaFile?> ReplaceFileAsync(
        Guid contentHierarchyItemId,
        Guid mediaFileId,
        string stagedPath,
        string outputFileName,
        MediaInfo mediaInfo,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a file association and keeps the host file.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="mediaFileId">The id of the media file</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> UnlinkFileAsync(
        Guid contentHierarchyItemId,
        Guid mediaFileId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches every content hierarchy item the action selects.
    /// </summary>
    /// <param name="patch">The selection of items and the fields to change</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<BulkResult> BulkPatchAsync(
        BulkPatch<ContentFilterCriteria, ContentHierarchyItemPatch> patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every content hierarchy item the action selects.
    /// </summary>
    /// <param name="action">The selection of items to act on</param>
    /// <param name="deleteFromLibrary">Whether the delete also removes the files from disk</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<BulkResult> BulkDeleteAsync(
        BulkAction<ContentFilterCriteria> action,
        bool deleteFromLibrary,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the files that a bulk delete removes from disk.
    /// </summary>
    /// <param name="action">The selection of items to delete</param>
    /// <param name="deleteFromLibrary">Whether the delete also removes the files from disk</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<ContentDeleteFile>> PreviewBulkDeleteAsync(
        BulkAction<ContentFilterCriteria> action,
        bool deleteFromLibrary,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the aggregate fulfillment counts for the content library.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ContentStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Hydrates content from an external provider id.
    /// </summary>
    /// <param name="providerType">The metadata provider type</param>
    /// <param name="externalIdKey">The external id source</param>
    /// <param name="externalId">The external id value</param>
    /// <param name="contentType">The content type slug</param>
    /// <param name="levelSlug">The target root level slug</param>
    /// <param name="rootFolderId">The target root folder id</param>
    /// <param name="monitoring">The monitoring override</param>
    /// <param name="qualityTierListId">The quality tier list id</param>
    /// <param name="requestedViaWishlistId">The requesting wishlist id</param>
    /// <param name="tags">The tags to apply</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The hydration result, or <see langword="null"/> when a resolver reports not-found</returns>
    Task<ContentHierarchyItem?> HydrateFromExternalIdAsync(
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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hydrates content from an external provider id, with the descendant structure the provider declares.
    /// </summary>
    /// <param name="providerType">The metadata provider type</param>
    /// <param name="externalIdKey">The external id source</param>
    /// <param name="externalId">The external id value</param>
    /// <param name="contentType">The content type slug</param>
    /// <param name="levelSlug">The target root level slug</param>
    /// <param name="rootFolderId">The target root folder id</param>
    /// <param name="monitoring">The monitoring override</param>
    /// <param name="qualityTierListId">The quality tier list id</param>
    /// <param name="requestedViaWishlistId">The requesting wishlist id</param>
    /// <param name="tags">The tags to apply</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The root item and the ids created by this hydration, or <see langword="null"/> when a resolver reports not-found</returns>
    Task<(ContentHierarchyItem Root, IReadOnlyCollection<Guid> CreatedHierarchyItemIds)?>
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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes and stores the fulfillment rollups of a changed hierarchy item and its ancestors.
    /// </summary>
    /// <param name="changedContentHierarchyItemId">The id of the item that changed</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="includeSelf">Whether the rollup covers the changed item as well as its ancestors</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ContentHierarchyItem?> MaterializeFulfillmentStatusAsync(
        Guid changedContentHierarchyItemId,
        Guid? actingUserId,
        bool includeSelf = false,
        CancellationToken cancellationToken = default);
}
