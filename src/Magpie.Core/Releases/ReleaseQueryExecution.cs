using System.Collections.Generic;

using Magpie.Models.Releases;

namespace Magpie.Core.Releases;

internal sealed record ReleaseQueryExecution
{
    public required IReadOnlyList<EvaluatedRelease> Results { get; init; }

    public required IReadOnlyList<ReleaseSearchPhase> ExecutedRoutes { get; init; }

    public int ConnectionsAttempted { get; init; }

    public int ConnectionsSucceeded { get; init; }

    public bool Partial { get; init; }

    public string? Warning { get; init; }
}
