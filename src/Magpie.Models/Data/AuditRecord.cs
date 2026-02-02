using System;

namespace Magpie.Models.Data;

/// <summary>
/// An audit record for historical tracking.
/// </summary>
public record AuditRecord
{
    /// <summary>
    /// The audit record id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The identifier of the affected entity, if applicable.
    /// </summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// The identifier of the parent entity, if applicable.
    /// </summary>
    public string? ParentEntityId { get; set; }

    /// <summary>
    /// The type of the affected entity.
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// The audit category.
    /// </summary>
    public required string Category { get; set; }

    /// <summary>
    /// The operation performed.
    /// </summary>
    public required string Operation { get; set; }

    /// <summary>
    /// Additional detail about the event.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// When the event occurred.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// The id of the user that performed the operation.
    /// </summary>
    public Guid? UserId { get; set; }
}
