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
    public required string Version { get; set; }

    /// <summary>
    /// The description of the runtime.
    /// </summary>
    public required string Runtime { get; set; }

    /// <summary>
    /// The description of the operating system.
    /// </summary>
    public required string OperatingSystem { get; set; }

    /// <summary>
    /// The architecture of the process.
    /// </summary>
    public required string Architecture { get; set; }

    /// <summary>
    /// The root directory of the Magpie installation.
    /// </summary>
    public required string SystemRoot { get; set; }

    /// <summary>
    /// The id of the local node.
    /// </summary>
    public required string NodeId { get; set; }

    /// <summary>
    /// The UTC start time of the host.
    /// </summary>
    public required DateTime StartedAt { get; set; }

    /// <summary>
    /// Whether the host can restart itself, which requires a supervising process manager.
    /// </summary>
    public required bool RestartSupported { get; set; }
}
