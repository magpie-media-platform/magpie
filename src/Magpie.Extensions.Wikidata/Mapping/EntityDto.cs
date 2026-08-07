using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.Mapping;

internal sealed record EntityDto
{
    public string? Id { get; init; }

    public bool Missing { get; init; }

    public Dictionary<string, MonolingualTextDto>? Labels { get; init; }

    public Dictionary<string, MonolingualTextDto>? Descriptions { get; init; }

    public Dictionary<string, List<MonolingualTextDto>>? Aliases { get; init; }

    public Dictionary<string, List<StatementDto>>? Claims { get; init; }
}
