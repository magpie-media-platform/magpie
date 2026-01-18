using System;

namespace Magpie.Models;

/// <summary>
/// Patch for <see cref="ServerConfig"/>.
/// </summary>
public sealed record ServerConfigPatch
{
    /// <inheritdoc cref="ServerConfig.DefaultLanguagePreference" />
    public FieldPatch<string?>? DefaultLanguagePreference { get; init; }
    /// <inheritdoc cref="ServerConfig.AutoUpdateEnabled" />
    public FieldPatch<bool>? AutoUpdateEnabled { get; init; }
    /// <inheritdoc cref="ServerConfig.BackupSchedule" />
    public FieldPatch<string>? BackupSchedule { get; init; }
    /// <inheritdoc cref="ServerConfig.BackupRetentionPeriod" />
    public FieldPatch<TimeSpan>? BackupRetentionPeriod { get; init; }
}
