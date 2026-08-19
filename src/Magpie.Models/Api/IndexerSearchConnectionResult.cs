using System;

using Magpie.Models.Enums;

namespace Magpie.Models.Api;

/// <summary>
/// The API representation of what one selected indexer connection did in a search.
/// </summary>
public sealed record IndexerSearchConnectionResult
{
    /// <summary>
    /// The id of the indexer connection.
    /// </summary>
    public required Guid ConnectionId { get; init; }

    /// <summary>
    /// The id of the indexer that owns the connection.
    /// </summary>
    public required Guid IndexerId { get; init; }

    /// <summary>
    /// The name of the indexer connection.
    /// </summary>
    public required string ConnectionName { get; init; }

    /// <summary>
    /// True when the connection is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The health status of the connection.
    /// </summary>
    public IntegrationHealthStatus Status { get; init; }

    /// <summary>
    /// True when the search sent the query to the connection.
    /// </summary>
    public bool Attempted { get; init; }

    /// <summary>
    /// The reason why the search did not query the connection, or <see langword="null"/>.
    /// </summary>
    public string? SkippedReason { get; init; }

    /// <summary>
    /// The error that the connection reported, or <see langword="null"/>.
    /// </summary>
    public string? Warning { get; init; }
}
