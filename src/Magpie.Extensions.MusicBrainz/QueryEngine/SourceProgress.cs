using System.Collections.Generic;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record SourceProgress
{
    internal string? Signature { get; set; }

    internal int Offset { get; set; }

    internal int Consumed { get; set; }

    internal bool Exhausted { get; set; }

    internal HashSet<string> Seen { get; set; } = [];
}
