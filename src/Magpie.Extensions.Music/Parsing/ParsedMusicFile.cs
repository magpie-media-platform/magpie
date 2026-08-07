namespace Magpie.Extensions.Music.Parsing;

internal sealed record ParsedMusicFile
{
    public string? Artist { get; init; }

    public required string Album { get; init; }

    public int? Year { get; init; }
}
