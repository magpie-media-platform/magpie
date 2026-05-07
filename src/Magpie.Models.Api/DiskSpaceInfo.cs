using System;

namespace Magpie.Models.Api;

/// <summary>
/// Disk space information for a root folder.
/// </summary>
public sealed record DiskSpaceInfo
{
    /// <summary>
    /// The root folder id.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The root folder display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The filesystem path.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Total space in bytes on the drive containing this path.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Free space in bytes on the drive containing this path.
    /// </summary>
    public long FreeBytes { get; init; }

    /// <summary>
    /// Used space in bytes (TotalBytes - FreeBytes).
    /// </summary>
    public long UsedBytes { get; init; }
}
