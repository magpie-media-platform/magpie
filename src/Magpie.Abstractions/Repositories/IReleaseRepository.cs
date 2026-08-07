using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for releases.
/// </summary>
public interface IReleaseRepository :
    ICrudRepository<Guid, Release>,
    IQueryRepository<Release, ReleaseQuery>,
    IBulkDeleteRepository<Release, ReleaseQuery>
{
    /// <summary>
    /// Records that a set of releases is present now.
    /// </summary>
    /// <param name="releaseIds">The ids of the releases to mark</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The number of releases that the mark changed</returns>
    Task<int> MarkObservedAsync(
        IReadOnlyCollection<Guid> releaseIds,
        CancellationToken cancellationToken = default);
}
