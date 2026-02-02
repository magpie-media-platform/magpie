using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Reference"/>.
/// </summary>
/// <param name="Id">The <see cref="Reference.Id"/> of the item to patch.</param>
/// <param name="Ordinal">The <see cref="Reference.Ordinal"/> patch</param>
/// <param name="Kind">The <see cref="Reference.Kind"/> patch</param>
/// <param name="Url">The <see cref="Reference.Url"/> patch</param>
/// <param name="DisplayName">The <see cref="Reference.DisplayName"/> patch</param>
/// <param name="Language">The <see cref="Reference.Language"/> patch</param>
public sealed record ReferencePatch(
    Guid Id,
    ScalarPatch<int>? Ordinal = null,
    ScalarPatch<string>? Kind = null,
    ScalarPatch<Uri>? Url = null,
    ScalarPatch<string?>? DisplayName = null,
    ScalarPatch<string?>? Language = null);
