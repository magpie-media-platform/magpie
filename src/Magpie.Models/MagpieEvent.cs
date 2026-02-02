using System;
using System.Collections.Generic;

namespace Magpie.Models;

/// <summary>
/// An event that occurred in the system.
/// </summary>
/// <param name="Id">The event id.</param>
/// <param name="Name">The event name (e.g., "content.added", "download.completed").</param>
/// <param name="Context">Context data associated with the event.</param>
/// <param name="OccurredAt">When the event occurred.</param>
/// <param name="PublishedAt">When the event was published.</param>
/// <param name="NodeId">The node that raised the event, if applicable.</param>
public sealed record MagpieEvent(
    Guid Id,
    string Name,
    IReadOnlyDictionary<string, string> Context,
    DateTime OccurredAt,
    DateTime PublishedAt,
    string? NodeId);
