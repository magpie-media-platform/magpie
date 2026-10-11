using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for root folders.
/// </summary>
public interface IRootFolderRepository :
    ICrudRepository<Guid, RootFolder>,
    IPatchRepository<Guid, RootFolder, RootFolderPatch>
{
    /// <summary>
    /// Gets the total size of the media files in each root folder.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The total size in bytes by root folder id</returns>
    Task<IReadOnlyDictionary<Guid, long>> GetContentSizesAsync(CancellationToken cancellationToken = default);
}
