using System;

namespace Magpie.Models.Data;

/// <summary>
/// A historical record of one audited operation.
/// </summary>
public record AuditRecord
{
    /// <summary>
    /// The unique id of the audit record.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The id of the audited entity.
    /// </summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// The id of the parent of the audited entity.
    /// </summary>
    public string? ParentEntityId { get; set; }

    /// <summary>
    /// The type of the audited entity.
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// The category of the audited operation.
    /// </summary>
    public required string Category { get; set; }

    /// <summary>
    /// The name of the audited operation.
    /// </summary>
    public required string Operation { get; set; }

    /// <summary>
    /// The detail text of the audited operation.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// The JSON data of the audited operation.
    /// </summary>
    public string? Data { get; set; }

    /// <summary>
    /// The UTC time of the audited operation.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// The id of the user who did the operation.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// The correlation id of the request.
    /// </summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>
    /// The id of the job execution that wrote the record, when a job wrote it.
    /// </summary>
    public Guid? JobExecutionId { get; set; }
}
