using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary.Mapping;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal interface ISearchEngine
{
    Task<QueryResult> SearchAsync(QueryRequest request, CancellationToken cancellationToken);

    Task<WorkResolution> ResolveWorkAsync(QueryRequest request, CancellationToken cancellationToken);

    Task<AuthorDto?> GetAuthorAsync(string authorOlid, CancellationToken cancellationToken);

    Task<EditionDto?> GetRepresentativeEditionAsync(WorkHit hit, CancellationToken cancellationToken);
}
