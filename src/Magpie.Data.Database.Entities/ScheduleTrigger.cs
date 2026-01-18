using System;

namespace Magpie.Data.Database.Entities;

public class ScheduleTrigger
{
    public Guid Id { get; set; }
    public Guid JobTemplateId { get; set; }
    public required string Name { get; set; }
    public required string Schedule { get; set; }
    public bool Enabled { get; set; }
    public string? Parameters { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }
}
