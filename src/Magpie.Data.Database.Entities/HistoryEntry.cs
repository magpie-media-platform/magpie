using System;

namespace Magpie.Data.Database.Entities;

public record HistoryEntry
{
    public Guid Id { get; set; }
    public string? EntityId { get; set; }
    public string? ParentEntityId { get; set; }
    public string? EntityType { get; set; }
    public required string Category { get; set; }
    public required string Operation { get; set; }
    public string? Description { get; set; }
    public bool Success { get; set; }
    public string? Result { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public Guid? UserId { get; set; }

    public User? User { get; set; }
}
