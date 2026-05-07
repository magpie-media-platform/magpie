using System;

namespace Magpie.Models.Api;

/// <summary>
/// A single application log entry.
/// </summary>
public sealed record LogEntry
{
    /// <summary>
    /// A unique identifier for this entry.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The UTC timestamp when this entry was recorded.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// The severity level: debug, info, warn, or error.
    /// </summary>
    public required string Level { get; init; }

    /// <summary>
    /// The logger category name (source component).
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// The log message text.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// The exception details, if an exception was logged.
    /// </summary>
    public string? Exception { get; init; }
}
