using System;

namespace Magpie.Abstractions.System;

/// <summary>
/// Provides access to Magpie system-level paths and configuration.
/// </summary>
public interface IMagpieSystem
{
    /// <summary>
    /// The root directory for the Magpie installation.
    /// </summary>
    string SystemRoot { get; }

    /// <summary>
    /// The unique id for this node.
    /// </summary>
    string NodeId { get; }

    /// <summary>
    /// The UTC time when the application started.
    /// </summary>
    DateTime StartedAt { get; }
}
