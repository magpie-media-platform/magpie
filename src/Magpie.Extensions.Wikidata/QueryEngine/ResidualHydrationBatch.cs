using System.Collections.Generic;

using Magpie.Extensions.Wikidata.Mapping;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record ResidualHydrationBatch
{
    public required IReadOnlyDictionary<string, EntityDto> Entities { get; init; }

    public required int RequestedCount { get; init; }

    public bool Failed { get; init; }

    public bool Truncated { get; init; }
}
