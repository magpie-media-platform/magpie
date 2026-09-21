namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record CriterionDisposition
{
    public required string Source { get; init; }

    public required CriterionDispositionKind Disposition { get; init; }

    public required string Reason { get; init; }
}
