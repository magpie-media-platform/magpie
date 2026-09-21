using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a <see cref="RemotePathMapping"/>.
/// </summary>
public sealed record RemotePathMappingPatch
{
    /// <summary>
    /// The id of the remote path mapping to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the download client host.
    /// </summary>
    public ScalarPatch<string>? Host { get; init; }

    /// <summary>
    /// The patch for the remote path.
    /// </summary>
    public ScalarPatch<string>? RemotePath { get; init; }

    /// <summary>
    /// The patch for the local path.
    /// </summary>
    public ScalarPatch<string>? LocalPath { get; init; }
}
