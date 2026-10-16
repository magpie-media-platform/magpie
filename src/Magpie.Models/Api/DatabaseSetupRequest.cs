using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Api;

/// <summary>
/// The request to write the database settings during first-run setup.
/// </summary>
public sealed record DatabaseSetupRequest
{
    /// <summary>
    /// The id of the database provider.
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// The connection string for the database, when the provider requires one.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// The roles of a node that joins a cluster.
    /// </summary>
    public IReadOnlyList<NodeRole>? Roles { get; init; }

    /// <summary>
    /// The job types that a node that joins a cluster runs.
    /// </summary>
    public IReadOnlyList<string> JobTypes { get; init; } = [];
}
