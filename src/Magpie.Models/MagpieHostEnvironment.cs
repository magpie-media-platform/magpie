using System;

namespace Magpie.Models;

/// <summary>
/// The fixed facts about the host process, set once at composition.
/// </summary>
/// <param name="NodeId">The id of this host among the nodes</param>
/// <param name="IsClustered">Whether the host is a member of a cluster</param>
/// <param name="IsSupervised">Whether a process manager supervises the host</param>
/// <param name="SystemRoot">The path of the root directory of the installation</param>
/// <param name="ExtensionsRoot">The path of the directory that holds the extensions</param>
/// <param name="ConfigRoot">The path of the directory that holds the user-owned configuration</param>
/// <param name="DataRoot">The path of the directory that holds the runtime data</param>
/// <param name="StartedAt">The UTC time when the host started</param>
public sealed record MagpieHostEnvironment(
    string NodeId,
    bool IsClustered,
    bool IsSupervised,
    string SystemRoot,
    string ExtensionsRoot,
    string ConfigRoot,
    string DataRoot,
    DateTime StartedAt);
