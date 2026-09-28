using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record QueryProgress
{
    internal string? Configuration { get; set; }

    internal bool ValidateOnly { get; set; }

    internal string? ResolvedSources { get; set; }

    internal int SeedOffset { get; set; }

    internal int CandidateOffset { get; set; }

    internal HashSet<string> Delivered { get; set; } = [];

    internal bool Exhausted { get; set; }

    internal QueryProgress Copy() => this with { Delivered = [.. Delivered] };
}
