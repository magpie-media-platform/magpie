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
}
