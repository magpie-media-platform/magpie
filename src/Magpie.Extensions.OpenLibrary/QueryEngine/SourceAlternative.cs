using System.Collections.Generic;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record SourceAlternative
{
    public required IReadOnlyList<SourceTerm> Terms { get; init; }
}
