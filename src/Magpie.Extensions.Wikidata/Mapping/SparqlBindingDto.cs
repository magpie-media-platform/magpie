namespace Magpie.Extensions.Wikidata.Mapping;

internal sealed record SparqlBindingDto
{
    public string? Type { get; init; }

    public string? Value { get; init; }
}
