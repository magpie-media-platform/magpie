namespace Magpie.Models.Enums;

/// <summary>
/// The kind of a database backup.
/// </summary>
public enum BackupKind
{
    /// <summary>
    /// A backup that an operator creates.
    /// </summary>
    Manual = 0,

    /// <summary>
    /// A backup that scheduled maintenance creates.
    /// </summary>
    Scheduled = 1
}
