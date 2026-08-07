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
}
