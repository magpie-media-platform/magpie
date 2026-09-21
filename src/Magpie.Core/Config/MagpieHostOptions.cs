using System.Collections.Generic;

namespace Magpie.Core.Config;

/// <summary>
/// The host-level options for Magpie.
/// </summary>
public sealed record MagpieHostOptions
{
    /// <summary>
    /// The configuration section name for these options.
    /// </summary>
    public const string SectionName = "Magpie";

    /// <summary>
    /// The cluster topology options.
    /// </summary>
    public ClusterHostOptions Cluster { get; init; } = new();

    /// <summary>
    /// The extension loading options.
    /// </summary>
    public ExtensionHostOptions Extensions { get; init; } = new();

    /// <summary>
    /// The runtime data directory options.
    /// </summary>
    public DataHostOptions Data { get; init; } = new();

    /// <summary>
    /// The browser origins that may call the API from another origin.
    /// </summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    /// <summary>
    /// The addresses and CIDR ranges of the reverse proxies whose forwarded headers the host accepts.
    /// </summary>
    public IReadOnlyList<string> KnownProxies { get; init; } = [];
}
