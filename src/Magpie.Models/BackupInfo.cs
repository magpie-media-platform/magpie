using System;

using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// The details of one database backup.
/// </summary>
public sealed record BackupInfo
{
    /// <summary>
    /// The id of the backup.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The name of the backup.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The creation time of the backup.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// The size of the backup in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// The kind of the backup.
    /// </summary>
    public BackupKind Kind { get; init; } = BackupKind.Manual;
}
