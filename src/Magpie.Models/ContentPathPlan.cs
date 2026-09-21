using System.Collections.Generic;

namespace Magpie.Models;

/// <summary>
/// The directories and file name that a content type generates for one content file.
/// </summary>
public sealed record ContentPathPlan
{
    /// <summary>
    /// The generated directories, from the root of the hierarchy to the leaf.
    /// </summary>
    public required IReadOnlyList<ContentPathPlanDirectory> Directories { get; set; }

    /// <summary>
    /// The generated file name, with its extension.
    /// </summary>
    public required string FileName { get; set; }
}
