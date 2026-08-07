namespace Magpie.Extensions.Wikidata.Mapping;

internal sealed record SparqlResponseDto
{
    public SparqlResultsDto? Results { get; init; }
}
