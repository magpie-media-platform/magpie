using System.Collections.Generic;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record FilterGroup
{
    public required IReadOnlyList<FilterAlternative> Alternatives { get; init; }

    public bool Negate { get; init; }
}
