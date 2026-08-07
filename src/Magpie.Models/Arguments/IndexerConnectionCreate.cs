using System.Collections.Generic;

namespace Magpie.Models.Arguments;

/// <summary>
/// The create arguments for a connection on an existing indexer.
/// </summary>
public sealed record IndexerConnectionCreate
{
    /// <summary>
    /// The display name of the connection.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The connection settings.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Settings { get; init; }

    /// <summary>
    /// Whether the connection is enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
