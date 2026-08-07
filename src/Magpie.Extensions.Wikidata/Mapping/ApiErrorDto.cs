namespace Magpie.Extensions.Wikidata.Mapping;

internal sealed record ApiErrorDto
{
    public string? Code { get; init; }

    public string? Info { get; init; }
}
