using System;

using Magpie.Models.Data;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for audit records.
/// </summary>
public sealed record AuditRecordQuery
{
    /// <summary>
    /// <inheritdoc cref="AuditRecord.EntityId" path="/summary"/>
    /// </summary>
    public string? EntityId { get; init; }

    /// <summary>
    /// <inheritdoc cref="AuditRecord.ParentEntityId" path="/summary"/>
    /// </summary>
    public string? ParentEntityId { get; init; }

    /// <summary>
    /// <inheritdoc cref="AuditRecord.EntityType" path="/summary"/>
    /// </summary>
    public string? EntityType { get; init; }

    /// <summary>
    /// <inheritdoc cref="AuditRecord.Category" path="/summary"/>
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// <inheritdoc cref="AuditRecord.Operation" path="/summary"/>
    /// </summary>
    public string? Operation { get; init; }

    /// <summary>
    /// <inheritdoc cref="AuditRecord.Detail" path="/summary"/>
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// <inheritdoc cref="AuditRecord.Timestamp" path="/summary"/>
    /// </summary>
    public Range<DateTime>? Timestamp { get; init; }

    /// <summary>
    /// The exclusive latest timestamp to match.
    /// </summary>
    public DateTime? TimestampBefore { get; init; }

    /// <summary>
    /// <inheritdoc cref="AuditRecord.UserId" path="/summary"/>
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// <inheritdoc cref="AuditRecord.CorrelationId" path="/summary"/>
    /// </summary>
    public Guid? CorrelationId { get; init; }
}
