using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Storage;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for root folders and their disk space.
/// </summary>
public interface IRootFolderService
{
    /// <summary>
    /// Lists the root folders.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<RootFolder>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a root folder by id, or <see langword="null"/> when absent.
    /// </summary>
    /// <param name="rootFolderId">The id of the root folder to return</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<RootFolder?> GetAsync(
        Guid rootFolderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a root folder.
    /// </summary>
    /// <param name="create">The values for the new root folder</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<RootFolder> CreateAsync(
        RootFolderCreate create,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a root folder.
    /// </summary>
    /// <param name="rootFolderId">The id of the root folder to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchAsync(
        Guid rootFolderId,
        RootFolderPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a root folder.
    /// </summary>
    /// <param name="rootFolderId">The id of the root folder to delete</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteAsync(
        Guid rootFolderId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the free and total disk space of the root folders.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<DiskSpaceResult> GetDiskSpaceAsync(CancellationToken cancellationToken = default);
}
