namespace Magpie.Models.Storage;

/// <summary>
/// Disk-space result for root folders.
/// </summary>
public sealed record DiskSpaceResult
{
    /// <summary>
    /// Disk-space entries by root folder.
    /// </summary>
    public required DiskSpaceInfo[] Folders { get; init; }

    /// <summary>
    /// Total bytes across distinct drives.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Free bytes across distinct drives.
    /// </summary>
    public long FreeBytes { get; init; }

    /// <summary>
    /// Used bytes across distinct drives.
    /// </summary>
    public long UsedBytes { get; init; }
}
