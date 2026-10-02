using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// The create arguments for a root folder.
/// </summary>
public sealed record RootFolderCreate
{
    /// <summary>
    /// The display name of the folder.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The path on the local file system.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// The content type slug of the folder.
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// Whether the folder is the default for the content type.
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// The default monitoring mode for new items in the folder.
    /// </summary>
    public MonitoringMode? DefaultMonitoring { get; init; }

    /// <summary>
    /// The template for renamed files.
    /// </summary>
    public string? RenameTemplate { get; init; }

    /// <summary>
    /// The <see cref="UpgradeFileHandling"/> for replaced files.
    /// </summary>
    public UpgradeFileHandling? UpgradeFileHandling { get; init; }

    /// <summary>
    /// Whether discovery associates confident matches with existing library content without user acceptance.
    /// </summary>
    public bool AutoImport { get; init; }

    /// <summary>
    /// The default quality tier lists keyed by hierarchy level.
    /// </summary>
    public Dictionary<string, RootFolderDefault> Defaults { get; init; } = [];
}
