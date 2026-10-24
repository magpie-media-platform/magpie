using System;

namespace Magpie.Models.Api;

/// <summary>
/// The management capabilities of the configured database provider.
/// </summary>
public sealed record DatabaseManagementInfo
{
    /// <summary>
    /// The id of the configured database provider.
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Whether the provider supports backup and restore.
    /// </summary>
    public bool BackupRestoreSupported { get; init; }

    /// <summary>
    /// Whether the provider supports database optimization.
    /// </summary>
    public bool OptimizeSupported { get; init; }

    /// <summary>
    /// The time of the last successful database optimization.
    /// </summary>
    public DateTime? LastOptimizedAt { get; init; }

    /// <summary>
    /// The reason that the database login or the process cannot create a backup.
    /// </summary>
    public string? BackupDeniedReason { get; init; }

    /// <summary>
    /// The reason that the database login or the process cannot restore a backup.
    /// </summary>
    public string? RestoreDeniedReason { get; init; }

    /// <summary>
    /// The reason that the database login or the process cannot delete a backup.
    /// </summary>
    public string? DeleteDeniedReason { get; init; }
}
