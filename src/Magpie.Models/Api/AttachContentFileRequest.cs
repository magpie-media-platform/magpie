namespace Magpie.Models.Api;

/// <summary>
/// A request to attach an existing host file to content.
/// </summary>
public sealed record AttachContentFileRequest
{
    /// <summary>
    /// The full path of the host file.
    /// </summary>
    public required string FilePath { get; init; }
}
