using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Storage;

namespace Magpie.Abstractions.Content;

/// <summary>
/// The service contract for content file operations.
/// </summary>
public interface IContentFileService
{
    /// <summary>
    /// Gets the configured root and initial path for the content file browser.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ContentFileBrowserLocation?> GetBrowserLocationAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a directory within the configured root of a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="path">The directory to list</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<FileSystemEntry>?> BrowseAsync(
        Guid contentHierarchyItemId,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches an existing host file to a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="filePath">The full path of the host file</param>
    /// <param name="actingUserId">The id of the user that attaches the file</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<AttachContentFileResult> AttachAsync(
        Guid contentHierarchyItemId,
        string filePath,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches a discovered library file, including a valid container fallback or an existing retry.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="filePath">The full path of the host file</param>
    /// <param name="actingUserId">The id of the user that attaches the file</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<AttachContentFileResult> AttachImportedAsync(
        Guid contentHierarchyItemId,
        string filePath,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches an existing host file and updates ancestor fulfillment in the same transaction.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="filePath">The full path of the host file</param>
    /// <param name="actingUserId">The id of the user that attaches the file</param>
    /// <param name="materializeFulfillmentAsync">The operation that updates ancestor fulfillment</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<AttachContentFileResult> AttachAsync(
        Guid contentHierarchyItemId,
        string filePath,
        Guid? actingUserId,
        Func<CancellationToken, Task> materializeFulfillmentAsync,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a file association and keeps the host file.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="mediaFileId">The id of the media file</param>
    /// <param name="actingUserId">The id of the user that unlinks the file</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<UnlinkContentFileResult> UnlinkAsync(
        Guid contentHierarchyItemId,
        Guid mediaFileId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a host file and removes its association.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="mediaFileId">The id of the media file</param>
    /// <param name="actingUserId">The id of the user that deletes the file</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<DeleteContentFileResult> DeleteAsync(
        Guid contentHierarchyItemId,
        Guid mediaFileId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles direct fulfillment against the accepted primary files of a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="actingUserId">The id of the user that changed the files</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> UpdateFulfillmentAsync(
        Guid contentHierarchyItemId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the rename that Magpie plans for a file, without a change on disk.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the leaf content item that owns the file</param>
    /// <param name="original">The file at its current path</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ContentRename> PreviewRenameAsync(
        Guid contentHierarchyItemId,
        FileInfo original,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the file to its target path and records the new path.
    /// </summary>
    /// <param name="rename">The planned rename to apply</param>
    /// <param name="userId">The id of the user that starts the rename, or null for a background operation</param>
    /// <param name="correlationId">The id that groups this rename with the operation that caused it</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<MediaFile> ExecuteRenameAsync(
        ContentRename rename,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a content path change and moves the affected files.
    /// </summary>
    /// <param name="contentHierarchyItemId">The content hierarchy item id</param>
    /// <param name="patch">The content hierarchy item changes</param>
    /// <param name="actingUserId">The user id, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> ChangePathAsync(
        Guid contentHierarchyItemId,
        ContentHierarchyItemPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the files in a content hierarchy subtree.
    /// </summary>
    /// <param name="items">The content hierarchy subtree</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task DeleteFilesAsync(
        IReadOnlyCollection<ContentHierarchyItem> items,
        CancellationToken cancellationToken = default);
}
