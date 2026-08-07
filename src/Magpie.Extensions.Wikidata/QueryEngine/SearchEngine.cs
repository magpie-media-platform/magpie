using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed class SearchEngine(IQueryPlanner planner, IQueryExecutor executor) : ISearchEngine
{
    public Task<QueryResult> SearchAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        var plan = planner.Plan(
            request.EntityKind,
            request.Criteria,
            request.Page,
            request.OrdinaryTerm,
            request.MaximumResults);
        return executor.SearchAsync(plan, request.Context, cancellationToken);
    }
}
