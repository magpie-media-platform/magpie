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
    public required Guid Id { get; set; }

    /// <summary>
    /// The kind of row that this entry comes from.
    /// </summary>
    public required AuditHistoryEntryKind Kind { get; set; }

    /// <summary>
    /// The UTC timestamp of the entry.
    /// </summary>
    public required DateTime Timestamp { get; set; }

    /// <summary>
    /// The correlation id that groups the rows of one run, when present.
    /// </summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>
    /// The display label of the entry.
    /// </summary>
    public required string Label { get; set; }

    /// <summary>
    /// The category of the entry.
    /// </summary>
    public required string Category { get; set; }

    /// <summary>
    /// The status text, when present.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// The detail text, when present.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// The id of the related entity, when present.
    /// </summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// The type of the related entity, when present.
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// The id of the user that acted, when present.
    /// </summary>
    public Guid? UserId { get; set; }
}
