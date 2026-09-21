namespace Magpie.Models;

/// <summary>
/// One generated directory of a content path plan, with the hierarchy level that owns it.
/// </summary>
public sealed record ContentPathPlanDirectory
{
    /// <summary>
    /// The hierarchy level slug that owns the directory.
    /// </summary>
    public required string LevelSlug { get; set; }

    /// <summary>
    /// The directory name, relative to the effective path of the owning node's parent.
    /// </summary>
    public required string Name { get; set; }
}
