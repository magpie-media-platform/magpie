using System;

namespace Magpie.Models.History;

/// <summary>
/// A history log entry recording an operation that occurred in the system.
/// </summary>
/// <param name="EntityId">The identifier of the entity involved, if applicable.</param>
/// <param name="EntityType">The type of entity involved, if applicable.</param>
/// <param name="Category">The category of the operation.</param>
/// <param name="Operation">The operation that was performed.</param>
/// <param name="Description">A description of what occurred.</param>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Result">A short result code or status.</param>
/// <param name="Detail">Detailed information about the result.</param>
/// <param name="Timestamp">When the operation occurred.</param>
/// <param name="UserId">The user who performed the operation, if applicable.</param>
public sealed record HistoryEntry(
    string? EntityId,
    string? EntityType,
    string Category,
    string Operation,
    string? Description,
    bool Success,
    string? Result,
    string? Detail,
    DateTimeOffset Timestamp,
    Guid? UserId);
