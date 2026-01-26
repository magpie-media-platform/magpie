using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;
using Magpie.Models.RootFolders;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing root folder configurations.
/// </summary>
public interface IRootFolderRepository :
    ICrudRepository<Guid, RootFolder>,
    IPatchRepository<Guid, RootFolder, RootFolderPatch>
{
    /// <summary>
    /// Get all root folders for a content type.
    /// </summary>
    /// <param name="contentTypeId">The content type identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<RootFolder>>> GetByContentTypeIdAsync(
        Guid contentTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a root folder by filesystem path.
    /// </summary>
    /// <param name="path">The filesystem path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<RootFolder?>> GetByPathAsync(string path, CancellationToken cancellationToken = default);
}
