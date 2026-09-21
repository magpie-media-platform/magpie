namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed record WorkResolution
{
    public required WorkResolutionKind Kind { get; init; }

    public WorkHit? Hit { get; init; }
}
