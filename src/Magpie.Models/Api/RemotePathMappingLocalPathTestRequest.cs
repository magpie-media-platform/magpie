namespace Magpie.Models.Api;

/// <summary>
/// The request for a read test of a local path in a remote path mapping.
/// </summary>
public sealed record RemotePathMappingLocalPathTestRequest
{
    /// <summary>
    /// The local directory path that Magpie must be able to read.
    /// </summary>
    public required string LocalPath { get; init; }
}
