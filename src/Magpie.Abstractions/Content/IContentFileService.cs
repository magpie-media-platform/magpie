using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Content;

/// <summary>
/// The service contract for content file operations.
/// </summary>
public interface IContentFileService
{
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
