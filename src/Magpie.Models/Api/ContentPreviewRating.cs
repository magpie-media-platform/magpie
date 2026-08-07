namespace Magpie.Models.Api;

/// <summary>
/// A content rating entry in a <see cref="ContentPreview"/>.
/// </summary>
public sealed record ContentPreviewRating
{
    /// <summary>
    /// The name of the body that issued the rating.
    /// </summary>
    public required string Body { get; init; }

    /// <summary>
    /// The rating value from the body.
    /// </summary>
    public required string Rating { get; init; }
}
