using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// The database status for first-run setup.
/// </summary>
public sealed record SetupDatabaseStatusResponse
{
    /// <summary>
    /// Whether the database is initialized.
    /// </summary>
    public required bool Initialized { get; init; }

    /// <summary>
    /// The id of the configured database provider.
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// The display names of the registered job types, by job type.
    /// </summary>
    public IReadOnlyDictionary<string, string> JobTypes { get; init; } = new Dictionary<string, string>();
}
