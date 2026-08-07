using System;

namespace Magpie.Models.Storage;

/// <summary>
/// Disk space for a root folder.
/// </summary>
public sealed record DiskSpaceInfo
{
    /// <summary>
    /// Root folder id.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Root folder display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Filesystem path.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Total bytes on the containing drive.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Free bytes on the containing drive.
    /// </summary>
    public long FreeBytes { get; init; }

    /// <summary>
    /// Used bytes on the containing drive.
    /// </summary>
    public long UsedBytes { get; init; }
}
