using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Image"/>.
/// </summary>
public sealed record ImagePatch
{
    /// <summary>
    /// The <see cref="Image.Id"/> of the item to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The <see cref="Image.Ordinal"/> to patch.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The <see cref="Image.Kind"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Kind { get; init; }

    /// <summary>
    /// The <see cref="Image.MimeType"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? MimeType { get; init; }

    /// <summary>
    /// The <see cref="Image.Url"/> to patch.
    /// </summary>
    public ScalarPatch<Uri>? Url { get; init; }

    /// <summary>
    /// The <see cref="Image.Source"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? Source { get; init; }

    /// <summary>
    /// The <see cref="Image.Language"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? Language { get; init; }

    /// <summary>
    /// The <see cref="Image.Width"/> to patch.
    /// </summary>
    public ScalarPatch<int?>? Width { get; init; }

    /// <summary>
    /// The <see cref="Image.Height"/> to patch.
    /// </summary>
    public ScalarPatch<int?>? Height { get; init; }
}
