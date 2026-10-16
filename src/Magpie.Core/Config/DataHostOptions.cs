namespace Magpie.Core.Config;

/// <summary>
/// The runtime data directory options for a Magpie host.
/// </summary>
public sealed record DataHostOptions
{
    /// <summary>
    /// The filesystem path to the mutable runtime data directory.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// The directory that holds the PostgreSQL binaries.
    /// </summary>
    public string? PostgreSqlToolsDirectory { get; init; }
}
