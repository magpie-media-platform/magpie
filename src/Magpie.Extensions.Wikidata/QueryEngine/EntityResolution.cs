namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record EntityResolution
{
    public required EntityResolutionKind Kind { get; init; }

    public string? Qid { get; init; }
}
