namespace Magpie.Models.Api;

/// <summary>
/// A file that a content delete removes from disk.
/// </summary>
public sealed record ContentDeleteFile
{
    /// <summary>
    /// The full path of the file on disk.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// The size of the file in bytes.
    /// </summary>
    public long Size { get; init; }
}
