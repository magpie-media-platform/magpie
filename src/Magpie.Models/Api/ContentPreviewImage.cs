namespace Magpie.Models.Api;

/// <summary>
/// An image entry in a <see cref="ContentPreview"/>.
/// </summary>
public sealed record ContentPreviewImage
{
    /// <summary>
    /// The type of the image.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// The absolute URL to the image.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// The language of the text in the image, when the image contains text.
    /// </summary>
    public string? Language { get; init; }
}
