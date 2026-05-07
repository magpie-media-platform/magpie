namespace Magpie.Models.Api;

/// <summary>
/// Request to browse a directory in the file system.
/// </summary>
public sealed record FileBrowserRequest
{
    /// <summary>
    /// The directory path to browse. Defaults to the system root.
    /// </summary>
    public string Path { get; init; } = "/";
}
