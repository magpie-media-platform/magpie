using System.Collections.Generic;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record SourcePage
{
    public required int NumFound { get; init; }

    public required bool NumFoundExact { get; init; }

    public required int Offset { get; init; }

    public required IReadOnlyList<WorkHit> Hits { get; init; }

    public required int SourceRowCount { get; init; }
}
