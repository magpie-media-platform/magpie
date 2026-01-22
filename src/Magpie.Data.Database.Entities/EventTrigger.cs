using System;

namespace Magpie.Data.Database.Entities;

public record EventTrigger
{
    public Guid Id { get; set; }
    public Guid JobTemplateId { get; set; }
    public required string Name { get; set; }
    public required string EventName { get; set; }
    public string? Filter { get; set; }
    public bool Enabled { get; set; }
    public string? Parameters { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }
}
