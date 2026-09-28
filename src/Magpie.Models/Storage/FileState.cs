using System;

namespace Magpie.Models.Storage;

/// <summary>
/// The size and write time read from a file on disk.
/// </summary>
public sealed record FileState
{
    /// <summary>
    /// The size of the file in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// The UTC time when the file contents last changed.
    /// </summary>
    public DateTime LastWriteTimeUtc { get; set; }
}
