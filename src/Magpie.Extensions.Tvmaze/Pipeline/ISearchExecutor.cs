using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.MetadataSearch;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal interface ISearchExecutor
{
    Task<MetadataSearchPage> ExecuteAsync(QueryPlan plan, CancellationToken cancellationToken);
}
