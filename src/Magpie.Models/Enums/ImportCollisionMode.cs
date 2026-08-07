namespace Magpie.Models.Enums;

/// <summary>
/// The collision policy for import destination files.
/// </summary>
public enum ImportCollisionMode
{
    /// <summary>
    /// If a planned destination file exists, the import stops.
    /// </summary>
    Fail = 0,

    /// <summary>
    /// The import skips the collision and keeps the existing file.
    /// </summary>
    Skip = 1,

    /// <summary>
    /// The import overwrites the existing file in place.
    /// </summary>
    Replace = 2,
}
