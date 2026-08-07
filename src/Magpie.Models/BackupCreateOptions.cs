using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// The options for a new database backup.
/// </summary>
public sealed record BackupCreateOptions
{
    /// <summary>
    /// The name for the backup.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The kind of backup to create.
    /// </summary>
    public BackupKind Kind { get; init; } = BackupKind.Manual;
}
