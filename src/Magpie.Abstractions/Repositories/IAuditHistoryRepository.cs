using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Paging;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for the merged audit-history feed.
/// </summary>
public interface IAuditHistoryRepository : IRepository
{
    /// <summary>
    /// Queries one page of the merged audit-history feed.
    /// </summary>
    /// <param name="query">The filter criteria, page number, and page size</param>
    /// <param name="parameterValueReferences">
    /// The job execution references, or <see langword="null"/> for no filter
    /// </param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<PagedList<AuditHistoryEntry>> QueryAsync(
        AuditHistoryQuery query,
        IReadOnlyCollection<string>? parameterValueReferences,
        CancellationToken cancellationToken = default);
}
