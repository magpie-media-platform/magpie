using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Preview"/>.
/// </summary>
public sealed record PreviewPatch
{
    /// <summary>
    /// The <see cref="Preview.Id"/> of the item to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The <see cref="Preview.Ordinal"/> to patch.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The <see cref="Preview.Kind"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Kind { get; init; }

    /// <summary>
    /// The <see cref="Preview.MimeType"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? MimeType { get; init; }

    /// <summary>
    /// The <see cref="Preview.Url"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Url { get; init; }

    /// <summary>
    /// The <see cref="Preview.Source"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? Source { get; init; }

    /// <summary>
    /// The <see cref="Preview.Title"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? Title { get; init; }

    /// <summary>
    /// The <see cref="Preview.ReleaseDate"/> to patch.
    /// </summary>
    public ScalarPatch<DateTime?>? ReleaseDate { get; init; }

    /// <summary>
    /// The <see cref="Preview.Language"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? Language { get; init; }
}
