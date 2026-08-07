namespace Magpie.Extensions.Books.Parsing;

internal sealed record ParsedBookFile
{
    public string? Author { get; init; }

    public required string Title { get; init; }

    public int? Year { get; init; }
}
