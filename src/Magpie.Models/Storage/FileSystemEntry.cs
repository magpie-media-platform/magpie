namespace Magpie.Models.Storage;

/// <summary>
/// One file or folder entry from the host file browser.
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
    /// The entry type.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// The size in bytes, present only for a file.
    /// </summary>
    public long? Size { get; init; }
}
