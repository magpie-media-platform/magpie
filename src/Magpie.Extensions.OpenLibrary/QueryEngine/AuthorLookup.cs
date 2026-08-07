namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record AuthorLookup
{
    public required bool Found { get; init; }

    public Mapping.AuthorDto? Author { get; init; }
}
