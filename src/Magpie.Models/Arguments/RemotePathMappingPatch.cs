using Magpie.Models.Data;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a <see cref="RemotePathMapping"/>.
/// </summary>
public sealed record RemotePathMappingPatch
{
    /// <summary>
    /// The patch for the download client host.
    /// </summary>
    public string? Host { get; init; }

    /// <summary>
    /// The patch for the remote path.
    /// </summary>
    public string? RemotePath { get; init; }

    /// <summary>
    /// The patch for the local path.
    /// </summary>
    public string? LocalPath { get; init; }
}
