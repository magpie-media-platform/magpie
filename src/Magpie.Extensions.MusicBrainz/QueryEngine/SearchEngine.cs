using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed class SearchEngine(IQueryPlanner planner, IQueryExecutor executor) : ISearchEngine
{
    public Task<QueryResult> SearchAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        var plan = planner.Plan(request);
        return executor.ExecuteAsync(plan, cancellationToken);
    }
}
