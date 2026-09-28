namespace Magpie.Models.Api;

/// <summary>
/// The rendered result of a rename mask.
/// </summary>
public sealed record RenameTokenPreview
{
    /// <summary>
    /// The rendered directory path under the folder-providing parent, or null when the file stays in it.
    /// </summary>
    public string? Directory { get; init; }

    /// <summary>
    /// The rendered file name, or null when the mask fails.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// The reason that the mask fails for the file, or null when it succeeds.
    /// </summary>
    public string? Error { get; init; }
}
