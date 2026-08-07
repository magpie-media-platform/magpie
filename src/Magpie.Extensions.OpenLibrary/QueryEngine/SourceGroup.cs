using System.Collections.Generic;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record SourceGroup
{
    public required IReadOnlyList<SourceAlternative> Alternatives { get; init; }
}
