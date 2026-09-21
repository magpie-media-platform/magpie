using System;

using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// One row of the merged audit-history feed, from a job execution or an audit record.
/// </summary>
public sealed record AuditHistoryEntry
{
    /// <summary>
    /// The id of the source row.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The kind of row that this entry comes from.
    /// </summary>
    public required AuditHistoryEntryKind Kind { get; init; }

    /// <summary>
    /// The UTC timestamp of the entry.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// The correlation id that groups the rows of one run, when present.
    /// </summary>
    public Guid? CorrelationId { get; init; }

    /// <summary>
    /// The display label of the entry.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// The category of the entry.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// The status text, when present.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// The detail text, when present.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// The id of the related entity, when present.
    /// </summary>
    public string? EntityId { get; init; }

    /// <summary>
    /// The type of the related entity, when present.
    /// </summary>
    public string? EntityType { get; init; }

    /// <summary>
    /// The id of the user that acted, when present.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// The id of the job execution that wrote the entry, when present.
    /// </summary>
    public Guid? JobExecutionId { get; init; }
}
