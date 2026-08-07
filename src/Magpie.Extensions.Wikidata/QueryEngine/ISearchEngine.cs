using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal interface ISearchEngine
{
    Task<QueryResult> SearchAsync(QueryRequest request, CancellationToken cancellationToken);
}
