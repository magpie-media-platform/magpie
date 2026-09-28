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
    /// Corrects a file association and preserves the file and its media information.
    /// </summary>
    /// <param name="contentHierarchyItemId">The current hierarchy owner</param>
    /// <param name="mediaFileId">The file to correct</param>
    /// <param name="request">The selected destination and expected file path</param>
    /// <param name="actingUserId">The user who corrects the association</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> ReassignAsync(
        Guid contentHierarchyItemId,
        Guid mediaFileId,
        ReassignContentFileRequest request,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a root folder has an explicit rename mask.
    /// </summary>
    /// <param name="rootFolderId">The root folder to check</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> HasRenameMaskAsync(Guid rootFolderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the registered rename tokens and previews a rename mask with example or content values.
    /// </summary>
    /// <param name="request">The content type or selected content and the mask to preview</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<RenameTokenReference> GetRenameTokenReferenceAsync(
        RenameTokenRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Previews an unassociated file against a read-only import lineage.
    /// </summary>
    /// <param name="lineage">The proposed content lineage from leaf to root</param>
    /// <param name="filePath">The existing file to associate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <param name="otherPlannedFiles">The other selected primary files for this content owner</param>
    Task<FileRenamePreview> PreviewImportRenameMaskAsync(
        IReadOnlyList<ContentHierarchyItem> lineage,
        string filePath,
        CancellationToken cancellationToken = default,
        IReadOnlyList<FileRenameEntry>? otherPlannedFiles = null);

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
    /// Gets the configured root and initial path for the content file browser.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ContentFileBrowserLocation?> GetBrowserLocationAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the absolute path of a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<string?> GetEffectivePathAsync(
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
    /// Plans a rename for a file, without a change on disk.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the leaf content item that owns the file</param>
    /// <param name="original">The file at its current path</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ContentRename> PreviewRenameAsync(
        Guid contentHierarchyItemId,
        FileInfo original,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Plans a rename for a file with known media info, without a change on disk.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the leaf content item that owns the file</param>
    /// <param name="original">The file at its current path</param>
    /// <param name="mediaInfo">The media info of the file for the rename tokens, or null when it is not known</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ContentRename> PreviewRenameAsync(
        Guid contentHierarchyItemId,
        FileInfo original,
        MediaInfo? mediaInfo,
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
