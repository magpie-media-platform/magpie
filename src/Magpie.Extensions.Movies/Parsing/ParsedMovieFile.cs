namespace Magpie.Extensions.Movies.Parsing;

internal sealed record ParsedMovieFile
{
    public required string Title { get; init; }

    public int? Year { get; init; }
}
