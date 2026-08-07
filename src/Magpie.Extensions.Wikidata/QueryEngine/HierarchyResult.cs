using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record HierarchyResult
{
    public required IReadOnlyList<HierarchyRow> Rows { get; init; }

    public required bool Truncated { get; init; }
}
