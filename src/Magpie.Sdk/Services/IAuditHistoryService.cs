using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Paging;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for the merged audit-history feed.
/// </summary>
public interface IAuditHistoryService
{
    /// <summary>
    /// Queries the merged audit-history feed.
    /// </summary>
    /// <param name="query">The filter criteria, page number, and page size</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<PagedList<AuditHistoryEntry>> QueryAsync(
        AuditHistoryQuery query,
        CancellationToken cancellationToken = default);
}
