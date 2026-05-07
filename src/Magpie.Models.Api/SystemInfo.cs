using System;

namespace Magpie.Models.Api;

/// <summary>
/// System information response.
/// </summary>
public sealed record SystemInfo
{
    /// <summary>
    /// The application version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// The runtime framework.
    /// </summary>
    public required string Runtime { get; init; }

    /// <summary>
    /// The operating system.
    /// </summary>
    public required string OperatingSystem { get; init; }

    /// <summary>
    /// The process architecture.
    /// </summary>
    public required string Architecture { get; init; }

    /// <summary>
    /// The Magpie installation root directory.
    /// </summary>
    public required string SystemRoot { get; init; }

    /// <summary>
    /// The unique node identifier.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// The UTC time when the application started.
    /// </summary>
    public required DateTime StartedAt { get; init; }
}
