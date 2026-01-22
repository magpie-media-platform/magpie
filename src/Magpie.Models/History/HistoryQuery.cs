using System;

namespace Magpie.Models.History;

/// <summary>
/// Filter criteria for searching history entries.
/// </summary>
/// <param name="EntityId">Filter by entity identifier.</param>
/// <param name="ParentEntityId">Filter by parent entity identifier.</param>
/// <param name="EntityType">Filter by entity type.</param>
/// <param name="Category">Filter by category.</param>
/// <param name="Operation">Filter by operation.</param>
/// <param name="Success">Filter by success status.</param>
/// <param name="Result">Filter by result code.</param>
/// <param name="DescriptionSearch">Search string for description (contains match).</param>
/// <param name="TimestampStart">Filter by minimum timestamp (inclusive).</param>
/// <param name="TimestampEnd">Filter by maximum timestamp (inclusive).</param>
/// <param name="UserId">Filter by user who performed the operation.</param>
public sealed record HistoryQuery(
    string? EntityId = null,
    string? ParentEntityId = null,
    string? EntityType = null,
    string? Category = null,
    string? Operation = null,
    bool? Success = null,
    string? Result = null,
    string? DescriptionSearch = null,
    DateTimeOffset? TimestampStart = null,
    DateTimeOffset? TimestampEnd = null,
    Guid? UserId = null);
