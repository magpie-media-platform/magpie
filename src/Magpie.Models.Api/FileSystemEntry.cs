namespace Magpie.Models.Api;

/// <summary>
/// Represents a file or folder entry in the file system.
/// </summary>
public sealed record FileSystemEntry
{
    /// <summary>
    /// The display name of the file or folder.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The full path to the file or folder.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// The entry type: "file" or "folder".
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// The file size in bytes. Only present for files.
    /// </summary>
    public long? Size { get; init; }
}
