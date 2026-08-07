using Magpie.Extensions.Wikidata.Mapping;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record EntityCacheValue
{
    public EntityDto? Entity { get; init; }
}
