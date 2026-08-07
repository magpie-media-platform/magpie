using System;
using System.Collections.Generic;

namespace Magpie.Models;

/// <summary>
/// An event that occurred in Magpie.
/// </summary>
/// <param name="Id">The id of the event</param>
/// <param name="Name">The name of the event</param>
/// <param name="Context">The context values of the event, keyed by context name</param>
/// <param name="OccurredAt">The time when the event occurred</param>
/// <param name="PublishedAt">The publication time of the event</param>
/// <param name="NodeId">The id of the node that raised the event, when any</param>
public sealed record MagpieEvent(
    Guid Id,
    string Name,
    IReadOnlyDictionary<string, string> Context,
    DateTime OccurredAt,
    DateTime PublishedAt,
    string? NodeId)
{
    /// <summary>
    /// The id of the job execution that published the event, when any.
    /// </summary>
    public Guid? JobExecutionId { get; init; }
}
