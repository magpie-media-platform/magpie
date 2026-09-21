using System;

namespace Magpie.Models;

/// <summary>
/// The build, runtime, and node information for the Magpie host.
/// </summary>
public sealed record SystemInfo
{
    /// <summary>
    /// The Magpie version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// The description of the runtime.
    /// </summary>
    public required string Runtime { get; init; }

    /// <summary>
    /// The description of the operating system.
    /// </summary>
    public required string OperatingSystem { get; init; }

    /// <summary>
    /// The architecture of the process.
    /// </summary>
    public required string Architecture { get; init; }

    /// <summary>
    /// The root directory of the Magpie installation.
    /// </summary>
    public required string SystemRoot { get; init; }

    /// <summary>
    /// The id of the local node.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// The UTC start time of the host.
    /// </summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>
    /// Whether the host can restart itself, which requires a supervising process manager.
    /// </summary>
    public required bool RestartSupported { get; init; }
}
