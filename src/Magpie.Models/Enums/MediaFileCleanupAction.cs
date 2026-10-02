namespace Magpie.Models.Enums;

/// <summary>
/// The action of a media file cleanup row.
/// </summary>
public enum MediaFileCleanupAction
{
    /// <summary>
    /// Moves the file association to the item that the binding matched. The file stays on disk.
    /// </summary>
    Reassign = 0,

    /// <summary>
    /// Removes the file association of a superseded copy or of an extra. The file stays on disk.
    /// </summary>
    Unlink = 1,

    /// <summary>
    /// Deletes a superseded copy from disk and removes its association.
    /// </summary>
    Delete = 2,

    /// <summary>
    /// Needs a decision that the plan cannot make, so apply skips it.
    /// </summary>
    Review = 3,

    /// <summary>
    /// Adds the other items that the file covers. The file stays on disk.
    /// </summary>
    Cover = 4,
}
