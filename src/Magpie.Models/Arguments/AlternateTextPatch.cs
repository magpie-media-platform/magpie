using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="AlternateText"/>.
/// </summary>
/// <param name="Id">The <see cref="AlternateText.Id"/> of the item to patch.</param>
/// <param name="Ordinal">The <see cref="AlternateText.Ordinal"/> patch</param>
/// <param name="Kind">The <see cref="AlternateText.Kind"/> patch</param>
/// <param name="Language">The <see cref="AlternateText.Language"/> patch</param>
/// <param name="Text">The <see cref="AlternateText.Text"/> patch</param>
public sealed record AlternateTextPatch(
    Guid Id,
    ScalarPatch<int>? Ordinal = null,
    ScalarPatch<string>? Kind = null,
    ScalarPatch<string?>? Language = null,
    ScalarPatch<string>? Text = null);

