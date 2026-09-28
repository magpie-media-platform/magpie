using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record SourceProgress
{
    internal string? Signature { get; set; }

    internal int Page { get; set; } = 1;

    internal int Consumed { get; set; }

    internal HashSet<int> Seen { get; set; } = [];

    internal HashSet<int> ExhaustedSources { get; set; } = [];

    internal bool Exhausted { get; set; }
}
