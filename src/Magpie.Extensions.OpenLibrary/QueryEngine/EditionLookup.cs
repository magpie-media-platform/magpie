namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record EditionLookup
{
    public required bool Found { get; init; }

    public Mapping.EditionDto? Edition { get; init; }
}
