using System.Collections.Generic;

using Magpie.Extensions.Wikidata.Mapping;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal readonly record struct SearchCacheResult(
    IReadOnlyList<Dictionary<string, SparqlBindingDto>> Rows,
    SearchCacheProvenance Provenance);
