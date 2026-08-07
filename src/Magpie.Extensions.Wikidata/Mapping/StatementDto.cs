using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.Mapping;

internal sealed record StatementDto
{
    public SnakDto? Mainsnak { get; init; }

    public string? Rank { get; init; }

    public Dictionary<string, List<SnakDto>>? Qualifiers { get; init; }
}
