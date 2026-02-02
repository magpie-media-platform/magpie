namespace Magpie.Models.Api;

/// <summary>
/// System information response.
/// </summary>
/// <param name="Version">The application version.</param>
/// <param name="Runtime">The runtime framework.</param>
/// <param name="OperatingSystem">The operating system.</param>
/// <param name="Architecture">The process architecture.</param>
/// <param name="SystemRoot">The Magpie installation root directory.</param>
/// <param name="NodeId">The unique node identifier.</param>
public sealed record SystemInfo(
    string Version,
    string Runtime,
    string OperatingSystem,
    string Architecture,
    string SystemRoot,
    string NodeId);
