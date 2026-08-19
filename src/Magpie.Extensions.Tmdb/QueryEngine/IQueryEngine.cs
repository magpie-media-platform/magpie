using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal interface IQueryEngine
{
    Task<QueryExecution> SearchAsync(QueryRequest request, CancellationToken cancellationToken);
}
