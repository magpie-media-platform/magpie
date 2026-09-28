using System;
using System.Collections.Generic;

namespace Magpie.Models.Releases;

/// <summary>
/// The outcome of one run of a release-search query plan.
/// </summary>
public sealed record ReleaseQueryExecution
{
    /// <summary>
    /// The assessed releases that the run collected.
    /// </summary>
    public required IReadOnlyList<EvaluatedRelease> Results { get; init; }

    /// <summary>
    /// The phases of the plan with the releases and the connection outcomes of each one.
    /// </summary>
    public required IReadOnlyList<ReleaseSearchPhase> ExecutedRoutes { get; init; }

    /// <summary>
    /// The number of connections that the run sent at least one query to.
    /// </summary>
    public int ConnectionsAttempted { get; init; }

    /// <summary>
    /// The number of connections that answered without an error.
    /// </summary>
    public int ConnectionsSucceeded { get; init; }

    /// <summary>
    /// True if one or more connections failed and the run returned an incomplete set.
    /// </summary>
    public bool Partial { get; init; }

    /// <summary>
    /// The earliest time to resume branches that an upstream cooldown deferred.
    /// </summary>
    public DateTimeOffset? RetryAt { get; init; }

    /// <summary>
    /// The message that describes the failure of the run, or null if every connection answered.
    /// </summary>
    public string? Warning { get; init; }
}
