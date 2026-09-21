namespace Magpie.Models.Enums;

/// <summary>
/// The kind of source that a library import candidate group collapses.
/// </summary>
public enum LibraryImportCandidateGroupKind
{
    /// <summary>
    /// The group is one directory of the root folder.
    /// </summary>
    Directory = 0,

    /// <summary>
    /// The group is one metadata match that its candidates share.
    /// </summary>
    Match = 1,

    /// <summary>
    /// The group is one parsed title that its candidates share.
    /// </summary>
    Query = 2
}
