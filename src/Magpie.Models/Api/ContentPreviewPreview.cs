namespace Magpie.Models.Api;

/// <summary>
/// A preview entry in a <see cref="ContentPreview"/>.
/// </summary>
public sealed record ContentPreviewPreview
{
    /// <summary>
    /// The kind of the preview.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// The slug of the embed strategy for the preview.
    /// </summary>
    public string? EmbedKind { get; init; }

    /// <summary>
    /// The absolute URL to the preview.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// The title of the preview, when a title is present.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The language of the preview, when a language is present.
    /// </summary>
    public string? Language { get; init; }
}
