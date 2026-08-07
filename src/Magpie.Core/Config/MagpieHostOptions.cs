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
}
