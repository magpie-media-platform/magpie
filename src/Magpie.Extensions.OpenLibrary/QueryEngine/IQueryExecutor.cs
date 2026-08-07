using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary.Mapping;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal interface IQueryExecutor
{
    Task<QueryResult> ExecuteSearchAsync(QueryPlan plan, CancellationToken cancellationToken);

    Task<WorkResolution> ResolveWorkAsync(QueryPlan plan, CancellationToken cancellationToken);

    Task<AuthorDto?> GetAuthorAsync(string authorOlid, CancellationToken cancellationToken);

    Task<EditionDto?> GetRepresentativeEditionAsync(WorkHit hit, CancellationToken cancellationToken);
}
