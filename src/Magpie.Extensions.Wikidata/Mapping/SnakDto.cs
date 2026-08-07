namespace Magpie.Extensions.Wikidata.Mapping;

internal sealed record SnakDto
{
    public string? SnakType { get; init; }

    public DataValueDto? DataValue { get; init; }
}
