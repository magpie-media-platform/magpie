namespace Magpie.Models.Enums;

/// <summary>
/// The data source of a rename token.
/// </summary>
public enum RenameTokenFamily
{
    /// <summary>
    /// The token comes from the source file name.
    /// </summary>
    File = 0,

    /// <summary>
    /// The token comes from the metadata of the content that owns the file.
    /// </summary>
    Metadata = 1,

    /// <summary>
    /// The token comes from a level of the content hierarchy.
    /// </summary>
    Hierarchy = 2,

    /// <summary>
    /// The token comes from the media information of the file.
    /// </summary>
    MediaInfo = 3,

    /// <summary>
    /// The token comes from the related subjects of the content.
    /// </summary>
    Relationship = 4,
}
