using System;

namespace Magpie.Models.Storage;

/// <summary>
/// The disk space of a root folder.
/// </summary>
public sealed record DiskSpaceInfo
{
    /// <summary>
    /// The id of the root folder.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The display name of the root folder.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The file system path of the root folder.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// The total bytes on the drive that holds the root folder.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// The free bytes on the drive that holds the root folder.
    /// </summary>
    public long FreeBytes { get; init; }

    /// <summary>
    /// The used bytes on the drive that holds the root folder.
    /// </summary>
    public long UsedBytes { get; init; }
}
