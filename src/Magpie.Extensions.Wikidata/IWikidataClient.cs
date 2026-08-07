using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;

namespace Magpie.Extensions.Wikidata;

internal interface IWikidataClient
{
    Task<IReadOnlyDictionary<string, EntityDto>> GetEntitiesAsync(
        IReadOnlyCollection<string> ids,
        RequestContext context,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> SearchEntitiesAsync(
        string search,
        RequestContext context,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>> ExecuteSparqlAsync(
        SparqlRequest request,
        RequestContext context,
        CancellationToken cancellationToken);
}
