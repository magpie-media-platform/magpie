using System;

namespace Magpie.Models.Releases;

/// <summary>
/// The outcome of one indexer connection in one search phase.
/// </summary>
public sealed record ReleaseSearchConnectionOutcome
{
    /// <summary>
    /// The id of the indexer connection.
    /// </summary>
    public required Guid ConnectionId { get; init; }

    /// <summary>
    /// The name of the indexer connection.
    /// </summary>
    public required string ConnectionName { get; init; }

    /// <summary>
    /// True when the phase sent the query to the connection.
    /// </summary>
    public bool Attempted { get; init; }

    /// <summary>
    /// The reason why the phase did not query the connection, or <see langword="null"/>.
    /// </summary>
    public string? SkippedReason { get; init; }

    /// <summary>
    /// The error that the connection reported, or <see langword="null"/>.
    /// </summary>
    public string? Error { get; init; }
}
