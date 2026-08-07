namespace Magpie.Models.Enums;

/// <summary>
/// The kind of a persisted media file.
/// </summary>
public enum FileKind
{
    /// <summary>
    /// A file with no known kind.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A video file.
    /// </summary>
    Video,

    /// <summary>
    /// An audio file.
    /// </summary>
    Audio,

    /// <summary>
    /// A subtitle file.
    /// </summary>
    Subtitle
}
