using System;

namespace Magpie.Data.Database.Entities;

public class Extension
{
    public Guid Id { get; set; }
    public required string Version { get; set; }
    public required string Name { get; set; }
    public string? TargetABI { get; set; }
    public required string FilePath { get; set; }
    public required string AssemblyName { get; set; }
    public bool Enabled { get; set; }
    public string? DisabledReason { get; set; }
    public int LoadOrder { get; set; }
    public bool Managed { get; set; }
    public string? NuGetId { get; set; }
    public string? PackageRepository { get; set; }
    public bool AutoUpgrade { get; set; }
    public DateTimeOffset? LastChecked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }
}
