using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a subject filter.
/// </summary>
public sealed record SubjectFilterPatch : TagsPatch
{
    /// <summary>
    /// The patch for the display name of the filter.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for the filter criteria.
    /// </summary>
    public ScalarPatch<SubjectFilterCriteria>? Criteria { get; init; }
}
