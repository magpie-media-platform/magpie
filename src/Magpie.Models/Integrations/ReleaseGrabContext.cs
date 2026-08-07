namespace Magpie.Models.Integrations;

/// <summary>
/// The context of a release grab.
/// </summary>
public sealed record ReleaseGrabContext
{
    /// <summary>
    /// The content type slug.
    /// </summary>
    public string? ContentType { get; init; }
}
