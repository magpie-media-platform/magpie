namespace Magpie.Models.Api;

/// <summary>
/// The root and initial directory of the content file browser.
/// </summary>
public sealed record ContentFileBrowserLocation
{
    /// <summary>
    /// The configured root that limits the browser.
    /// </summary>
    public required string RootPath { get; init; }

    /// <summary>
    /// The directory where the browser starts.
    /// </summary>
    public required string InitialPath { get; init; }
}
