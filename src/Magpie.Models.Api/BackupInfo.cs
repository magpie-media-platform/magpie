using System;

namespace Magpie.Models.Api;

/// <summary>
/// Information about a database backup.
/// </summary>
public sealed record BackupInfo
{
    /// <summary>
    /// The backup identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The human-readable name for the backup.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The timestamp when the backup was created.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// The size of the backup file in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }
}
