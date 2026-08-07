using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal interface IQueryExecutor
{
    Task<QueryExecution> ExecuteAsync(QueryPlan plan, RequestContext context, CancellationToken cancellationToken);
}
