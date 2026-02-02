using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Preview"/>.
/// </summary>
/// <param name="Id">The <see cref="Preview.Id"/> of the item to patch.</param>
/// <param name="Ordinal">The <see cref="Preview.Ordinal"/> patch</param>
/// <param name="Kind">The <see cref="Preview.Kind"/> patch</param>
/// <param name="MimeType">The <see cref="Preview.MimeType"/> patch</param>
/// <param name="Url">The <see cref="Preview.Url"/> patch</param>
/// <param name="Source">The <see cref="Preview.Source"/> patch</param>
/// <param name="Title">The <see cref="Preview.Title"/> patch</param>
/// <param name="ReleaseDate">The <see cref="Preview.ReleaseDate"/> patch</param>
/// <param name="Language">The <see cref="Preview.Language"/> patch</param>
public sealed record PreviewPatch(
    Guid Id,
    ScalarPatch<int>? Ordinal = null,
    ScalarPatch<string>? Kind = null,
    ScalarPatch<string?>? MimeType = null,
    ScalarPatch<string>? Url = null,
    ScalarPatch<string?>? Source = null,
    ScalarPatch<string?>? Title = null,
    ScalarPatch<DateTime?>? ReleaseDate = null,
    ScalarPatch<string?>? Language = null);
