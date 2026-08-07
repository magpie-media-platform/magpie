namespace Magpie.Models.Enums;

/// <summary>
/// The file transfer mode of the import pipeline.
/// </summary>
public enum ImportMode
{
    /// <summary>
    /// The import moves the source file to the destination and the source file is gone.
    /// </summary>
    Move = 0,

    /// <summary>
    /// The import copies the source file to the destination and keeps the source file.
    /// </summary>
    Copy = 1
}
