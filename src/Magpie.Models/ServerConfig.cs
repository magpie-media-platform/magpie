using System;

namespace Magpie.Models;

/// <summary>
/// User-configurable server settings stored in the database.
/// </summary>
/// <param name="DefaultLanguagePreference">The default language preference for new content.</param>
/// <param name="AutoUpdateEnabled">Whether automatic updates are enabled.</param>
/// <param name="BackupSchedule">The schedule for automatic backups.</param>
/// <param name="BackupRetentionPeriod">The retention period for backups.</param>
public sealed record ServerConfig(
    string? DefaultLanguagePreference,
    bool AutoUpdateEnabled,
    string BackupSchedule,
    TimeSpan BackupRetentionPeriod);
