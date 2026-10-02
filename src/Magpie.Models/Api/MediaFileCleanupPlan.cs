using System.Collections.Generic;

namespace Magpie.Models.Api;

/// <summary>
/// A read-only list of media file cleanup actions. Applying it changes only the rows that an operator approves.
/// </summary>
public sealed record MediaFileCleanupPlan
{
    /// <summary>
    /// The files on a container item that the binding matched to a child item. A reassign deletes nothing.
    /// </summary>
    public List<MediaFileCleanupRow> Reassign { get; set; } = [];

    /// <summary>
    /// The files on an item whose names cover sibling items that the files do not cover yet.
    /// </summary>
    public List<MediaFileCleanupRow> Cover { get; set; } = [];

    /// <summary>
    /// The copies that a better copy of the same item supersedes, and the extras that are stored as primary files.
    /// An approved row can unlink the file. Only a superseded copy can also be deleted from disk.
    /// </summary>
    public List<MediaFileCleanupRow> Unlink { get; set; } = [];

    /// <summary>
    /// The files that need a decision the plan cannot make.
    /// </summary>
    public List<MediaFileCleanupRow> Review { get; set; } = [];
}
