using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="ContentFilter"/>.
/// </summary>
public sealed record ContentFilterPatch : TagsPatch
{
    /// <summary>
    /// The <see cref="ContentFilter.Name"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The <see cref="ContentFilter.Criteria"/> to patch.
    /// </summary>
    public ScalarPatch<ContentFilterCriteria>? Criteria { get; init; }

    /// <summary>
    /// The <see cref="ContentFilter.OwnerLock"/> to patch.
    /// </summary>
    public ScalarPatch<bool?>? OwnerLock { get; init; }
}
