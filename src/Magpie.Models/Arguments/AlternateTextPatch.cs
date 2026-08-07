using System;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for an alternate text entry.
/// </summary>
public sealed record AlternateTextPatch
{
    /// <summary>
    /// The id of the alternate text entry to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the position in the display order.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The patch for the kind of text.
    /// </summary>
    public ScalarPatch<string>? Kind { get; init; }

    /// <summary>
    /// The patch for the language code of the text.
    /// </summary>
    public ScalarPatch<string?>? Language { get; init; }

    /// <summary>
    /// The patch for the alternate text.
    /// </summary>
    public ScalarPatch<string>? Text { get; init; }
}
