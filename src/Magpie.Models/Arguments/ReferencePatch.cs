using System;

using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a reference.
/// </summary>
public sealed record ReferencePatch
{
    /// <summary>
    /// The id of the reference to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the position in the display order.
    /// </summary>
    public ScalarPatch<int>? Ordinal { get; init; }

    /// <summary>
    /// The patch for the kind of reference.
    /// </summary>
    public ScalarPatch<string>? Kind { get; init; }

    /// <summary>
    /// The patch for the name of the source.
    /// </summary>
    public ScalarPatch<string>? Source { get; init; }

    /// <summary>
    /// The patch for the reference URL.
    /// </summary>
    public ScalarPatch<Uri>? Url { get; init; }

    /// <summary>
    /// The patch for the display name of the reference.
    /// </summary>
    public ScalarPatch<string?>? DisplayName { get; init; }

    /// <summary>
    /// The patch for the language code.
    /// </summary>
    public ScalarPatch<string?>? Language { get; init; }
}
