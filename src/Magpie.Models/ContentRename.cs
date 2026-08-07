using System.IO;

using Magpie.Models.Data;

namespace Magpie.Models;

/// <summary>
/// A planned move of one media file to its computed name.
/// </summary>
public sealed record ContentRename
{
    /// <summary>
    /// The file at its current path.
    /// </summary>
    public required FileInfo Original { get; init; }

    /// <summary>
    /// The absolute target path for the file.
    /// </summary>
    public required FileInfo Rename { get; init; }

    /// <summary>
    /// The leaf content item that owns the file.
    /// </summary>
    public required ContentHierarchyItem Item { get; init; }
}
