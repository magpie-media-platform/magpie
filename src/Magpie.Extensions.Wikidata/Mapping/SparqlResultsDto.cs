using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.Mapping;

internal sealed record SparqlResultsDto
{
    public List<Dictionary<string, SparqlBindingDto>>? Bindings { get; init; }
}
