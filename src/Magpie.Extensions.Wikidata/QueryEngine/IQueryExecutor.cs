using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal interface IQueryExecutor
{
    Task<QueryResult> SearchAsync(
        QueryPlan plan,
        RequestContext context,
        CancellationToken cancellationToken);

    Task<HierarchyResult> GetHierarchyAsync(
        EntityKind childKind,
        string parentQid,
        string? showQid,
        RequestContext context,
        CancellationToken cancellationToken);
}
