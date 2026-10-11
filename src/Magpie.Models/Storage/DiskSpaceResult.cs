namespace Magpie.Models.Storage;

/// <summary>
/// The disk space of the root folders.
/// </summary>
public sealed record DiskSpaceResult
{
    /// <summary>
    /// The disk space of each root folder.
    /// </summary>
    public required DiskSpaceInfo[] Folders { get; init; }
}
