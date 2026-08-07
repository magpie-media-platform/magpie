using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.Mapping;

internal sealed record EntitiesResponseDto
{
    public Dictionary<string, EntityDto>? Entities { get; init; }
}
