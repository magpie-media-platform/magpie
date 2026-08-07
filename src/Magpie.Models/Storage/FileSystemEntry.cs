namespace Magpie.Models.Storage;

/// <summary>
/// One file or folder entry from the host file browser.
/// </summary>
public sealed record FileSystemEntry
{
    /// <summary>
    /// The display name of the file or folder.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The full path to the file or folder.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// The entry type: <c>file</c> or <c>folder</c>.
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// The size in bytes, present only for a file.
    /// </summary>
    public long? Size { get; set; }
}
