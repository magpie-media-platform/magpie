using System.Collections.Generic;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record SourceTerm
{
    public required string Field { get; init; }

    public required IReadOnlyList<string> Values { get; init; }

    public bool Negate { get; init; }

    public bool RangeSyntax { get; init; }

    public WildcardPlacement WildcardPlacement { get; init; }
}
