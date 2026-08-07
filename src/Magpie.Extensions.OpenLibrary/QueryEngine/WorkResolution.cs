using System.Collections.Generic;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record WorkResolution
{
    public required WorkResolutionKind Kind { get; init; }

    public WorkHit? Hit { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
