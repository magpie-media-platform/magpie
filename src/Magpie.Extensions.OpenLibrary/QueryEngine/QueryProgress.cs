using System.Collections.Generic;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record QueryProgress
{
    internal string? Configuration { get; set; }

    internal bool ValidateOnly { get; set; }

    internal int Offset { get; set; }

    internal int Consumed { get; set; }

    internal HashSet<string> Seen { get; set; } = [];

    internal bool Exhausted { get; set; }

    internal QueryProgress Copy() => this with { Seen = [.. Seen] };
}
