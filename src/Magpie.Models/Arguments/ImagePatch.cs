using System;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for an image.
/// </summary>
public sealed record ImagePatch
{
    /// <summary>
    /// The id of the image to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the position in the display order.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The patch for the kind of image.
    /// </summary>
    public ScalarPatch<string>? Type { get; init; }

    /// <summary>
    /// The patch for the MIME type.
    /// </summary>
    public ScalarPatch<string?>? MimeType { get; init; }

    /// <summary>
    /// The patch for the image URL.
    /// </summary>
    public ScalarPatch<Uri>? Url { get; init; }

    /// <summary>
    /// The patch for the name of the source.
    /// </summary>
    public ScalarPatch<string?>? Source { get; init; }

    /// <summary>
    /// The patch for the language code.
    /// </summary>
    public ScalarPatch<string?>? Language { get; init; }

    /// <summary>
    /// The patch for the width in pixels.
    /// </summary>
    public ScalarPatch<int?>? Width { get; init; }

    /// <summary>
    /// The patch for the height in pixels.
    /// </summary>
    public ScalarPatch<int?>? Height { get; init; }
}
