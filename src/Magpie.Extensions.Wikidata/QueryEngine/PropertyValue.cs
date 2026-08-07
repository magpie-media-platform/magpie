namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record PropertyValue
{
    public required string PropertyId { get; init; }

    public required string Value { get; init; }
}
