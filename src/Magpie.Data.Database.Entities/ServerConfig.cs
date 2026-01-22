using System;

namespace Magpie.Data.Database.Entities;

public record ServerConfig
{
    public required int Id { get; set; }
    public string? DefaultLanguagePreference { get; set; }
    public bool AutoUpdateEnabled { get; set; }
    public required string BackupSchedule { get; set; }
    public required TimeSpan BackupRetentionPeriod { get; set; }
}
