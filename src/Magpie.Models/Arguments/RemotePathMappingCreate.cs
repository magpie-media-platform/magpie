namespace Magpie.Models.Arguments;

/// <summary>
/// The create arguments for a remote path mapping.
/// </summary>
public sealed record RemotePathMappingCreate
{
    /// <summary>
    /// The download client host that the mapping applies to.
    /// </summary>
    public required string Host { get; init; }

    /// <summary>
    /// The path as the download client reports it.
    /// </summary>
    public required string RemotePath { get; init; }

    /// <summary>
    /// The matching path on the local file system.
    /// </summary>
    public required string LocalPath { get; init; }
}
