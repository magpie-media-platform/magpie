namespace Magpie.Extensions.Wikidata.Mapping;

internal sealed record MonolingualTextDto
{
    public string? Language { get; init; }

    public string? Value { get; init; }
}
