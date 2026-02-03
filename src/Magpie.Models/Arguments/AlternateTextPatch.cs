using System;

using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="AlternateText"/>.
/// </summary>
public sealed record AlternateTextPatch
{
    /// <summary>
    /// The <see cref="AlternateText.Id"/> of the item to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The <see cref="AlternateText.Ordinal"/> patch.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The <see cref="AlternateText.Kind"/> patch.
    /// </summary>
    public ScalarPatch<string>? Kind { get; init; }

    /// <summary>
    /// The <see cref="AlternateText.Language"/> patch.
    /// </summary>
    public ScalarPatch<string?>? Language { get; init; }

    /// <summary>
    /// The <see cref="AlternateText.Text"/> patch.
    /// </summary>
    public ScalarPatch<string>? Text { get; init; }
}

