using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Image"/>.
/// </summary>
/// <param name="Id">The <see cref="Image.Id"/> of the item to patch.</param>
/// <param name="Ordinal">The <see cref="Image.Ordinal"/> patch</param>
/// <param name="Kind">The <see cref="Image.Kind"/> patch</param>
/// <param name="MimeType">The <see cref="Image.MimeType"/> patch</param>
/// <param name="Url">The <see cref="Image.Url"/> patch</param>
/// <param name="Source">The <see cref="Image.Source"/> patch</param>
/// <param name="Language">The <see cref="Image.Language"/> patch</param>
/// <param name="Width">The <see cref="Image.Width"/> patch</param>
/// <param name="Height">The <see cref="Image.Height"/> patch</param>
public sealed record ImagePatch(
    Guid Id,
    ScalarPatch<int>? Ordinal = null,
    ScalarPatch<string>? Kind = null,
    ScalarPatch<string?>? MimeType = null,
    ScalarPatch<Uri>? Url = null,
    ScalarPatch<string?>? Source = null,
    ScalarPatch<string?>? Language = null,
    ScalarPatch<int?>? Width = null,
    ScalarPatch<int?>? Height = null);
