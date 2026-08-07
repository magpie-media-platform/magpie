using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a content filter.
/// </summary>
public sealed record ContentFilterPatch : TagsPatch
{
    /// <summary>
    /// The patch for the display name of the filter.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for the filter criteria.
    /// </summary>
    public ScalarPatch<ContentFilterCriteria>? Criteria { get; init; }

    /// <summary>
    /// The patch for whether only the owner can edit the filter.
    /// </summary>
    public ScalarPatch<bool?>? OwnerLock { get; init; }
}
