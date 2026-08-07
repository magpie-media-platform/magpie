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
    public bool BackupRestoreSupported { get; init; } = false;

    /// <summary>
    /// Whether the provider supports database optimization.
    /// </summary>
    public bool OptimizeSupported { get; init; } = false;

    /// <summary>
    /// The time of the last successful database optimization.
    /// </summary>
    public DateTime? LastOptimizedAt { get; init; } = default;
}
