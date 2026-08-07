namespace Magpie.Extensions.Tv.Parsing;

internal sealed record ParsedTvFile
{
    public required string Title { get; init; }

    public int? Year { get; init; }

    public int? Season { get; init; }

    public int? Episode { get; init; }
}
