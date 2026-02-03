using Magpie.Models.Data;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// Patch for <see cref="SubjectFilter"/>.
/// </summary>
public sealed record SubjectFilterPatch : TagsPatch
{
    /// <summary>
    /// The <see cref="SubjectFilter.Name"/> to patch.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The <see cref="SubjectFilter.Criteria"/> to patch.
    /// </summary>
    public ScalarPatch<SubjectFilterCriteria>? Criteria { get; init; }
}
