using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Models.Releases;

/// <summary>
/// One ordered request node of a release search plan.
/// </summary>
public sealed record ReleaseSearchPhase
{
    /// <summary>
    /// The search request of this phase.
    /// </summary>
    public required ReleaseSearchRequest Request { get; init; }

    /// <summary>
    /// The content facts that the request uses.
    /// </summary>
    public required IReadOnlyList<ContentFact> ReleaseFacts { get; init; }

    /// <summary>
    /// The releases that the request returned.
    /// </summary>
    public IReadOnlyList<Release> Releases { get; init; } = [];

    /// <summary>
    /// The number of connections that the phase sent at least one query to.
    /// </summary>
    public int ConnectionsAttempted { get; init; }

    /// <summary>
    /// The number of connections that answered without an error.
    /// </summary>
    public int ConnectionsSucceeded { get; init; }

    /// <summary>
    /// The outcome of each connection that the phase considered.
    /// </summary>
    public IReadOnlyList<ReleaseSearchConnectionOutcome> Connections { get; init; } = [];

    /// <summary>
    /// The releases of the phase after the executor coalesced the duplicates.
    /// </summary>
    public IReadOnlyList<ReleaseSearchCandidate> Candidates { get; init; } = [];
}
