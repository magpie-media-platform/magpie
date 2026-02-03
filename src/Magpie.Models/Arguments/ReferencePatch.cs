using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="Reference"/>.
/// </summary>
public sealed record ReferencePatch
{
    /// <summary>
    /// The <see cref="Reference.Id"/> of the item to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The <see cref="Reference.Ordinal"/> to patch.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The <see cref="Reference.Kind"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Kind { get; init; }

    /// <summary>
    /// The <see cref="Reference.Url"/> to patch.
    /// </summary>
    public ScalarPatch<Uri>? Url { get; init; }

    /// <summary>
    /// The <see cref="Reference.DisplayName"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? DisplayName { get; init; }

    /// <summary>
    /// The <see cref="Reference.Language"/> to patch.
    /// </summary>
    public ScalarPatch<string?>? Language { get; init; }
}
