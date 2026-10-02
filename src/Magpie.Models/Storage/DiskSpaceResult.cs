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

    /// <summary>
    /// The total bytes across the distinct drives.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// The free bytes across the distinct drives.
    /// </summary>
    public long FreeBytes { get; init; }

    /// <summary>
    /// The used bytes across the distinct drives.
    /// </summary>
    public long UsedBytes { get; init; }
}
