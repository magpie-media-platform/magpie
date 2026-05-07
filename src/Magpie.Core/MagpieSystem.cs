using System;

using Magpie.Abstractions.System;

namespace Magpie.Core;

/// <summary>
/// Default <see cref="IMagpieSystem"/> implementation backed by the host content root and
/// the local machine name.
/// </summary>
/// <remarks>
/// Registered as a singleton at host startup. <see cref="StartedAt"/> captures the moment
/// the type is constructed, so it reflects host start when DI builds the service provider.
/// </remarks>
public sealed class MagpieSystem : IMagpieSystem
{
    /// <summary>
    /// Initialize a new <see cref="MagpieSystem"/> rooted at the given path.
    /// </summary>
    /// <param name="systemRoot">The absolute path to the Magpie installation root.</param>
    /// <param name="nodeId">Optional node id. Falls back to <see cref="Environment.MachineName"/>.</param>
    public MagpieSystem(string systemRoot, string? nodeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemRoot);

        SystemRoot = systemRoot;
        NodeId = string.IsNullOrWhiteSpace(nodeId) ? Environment.MachineName : nodeId;
        StartedAt = DateTime.UtcNow;
    }

    /// <inheritdoc />
    public string SystemRoot { get; }

    /// <inheritdoc />
    public string NodeId { get; }

    /// <inheritdoc />
    public DateTime StartedAt { get; }
}
