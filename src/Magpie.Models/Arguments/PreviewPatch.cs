using System;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a preview.
/// </summary>
public sealed record PreviewPatch
{
    /// <summary>
    /// The id of the preview to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the position in the display order.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The patch for the kind of preview.
    /// </summary>
    public ScalarPatch<string>? Kind { get; init; }

    /// <summary>
    /// The patch for the MIME type.
    /// </summary>
    public ScalarPatch<string?>? MimeType { get; init; }

    /// <summary>
    /// The patch for the preview URL.
    /// </summary>
    public ScalarPatch<Uri>? Url { get; init; }

    /// <summary>
    /// The patch for the name of the source.
    /// </summary>
    public ScalarPatch<string?>? Source { get; init; }

    /// <summary>
    /// The patch for the title of the preview.
    /// </summary>
    public ScalarPatch<string?>? Title { get; init; }

    /// <summary>
    /// The patch for the release date.
    /// </summary>
    public ScalarPatch<DateTime?>? ReleaseDate { get; init; }

    /// <summary>
    /// The patch for the language code.
    /// </summary>
    public ScalarPatch<string?>? Language { get; init; }
}
