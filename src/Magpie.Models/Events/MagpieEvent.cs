using System;
using System.Collections.Generic;

namespace Magpie.Models.Events;

/// <summary>
/// An event that occurred in the system.
/// </summary>
/// <param name="Id">The unique identifier for this event instance.</param>
/// <param name="Name">The event name (e.g., "content.added", "download.completed").</param>
/// <param name="Context">Arbitrary context data associated with the event.</param>
/// <param name="OccurredAt">When the event actually happened.</param>
/// <param name="PublishedAt">When the event was published.</param>
/// <param name="NodeId">The node that raised the event, for multi-node setups.</param>
public sealed record MagpieEvent(
    Guid Id,
    string Name,
    IReadOnlyDictionary<string, string> Context,
    DateTimeOffset OccurredAt,
    DateTimeOffset PublishedAt,
    string? NodeId);
