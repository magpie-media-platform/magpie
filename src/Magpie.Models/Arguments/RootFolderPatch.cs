using System;
using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;

namespace Magpie.Models.Arguments;

/// <summary>
/// The partial-update arguments for a root folder.
/// </summary>
public sealed record RootFolderPatch
{
    /// <summary>
    /// The id of the root folder to patch.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The patch for the display name of the folder.
    /// </summary>
    public ScalarPatch<string>? Name { get; init; }

    /// <summary>
    /// The patch for the path on the local file system.
    /// </summary>
    public ScalarPatch<string>? Path { get; init; }

    /// <summary>
    /// The patch for the content type slug of the folder.
    /// </summary>
    public ScalarPatch<string>? ContentType { get; init; }

    /// <summary>
    /// The patch for whether the folder is the default for the content type.
    /// </summary>
    public ScalarPatch<bool>? IsDefault { get; init; }

    /// <summary>
    /// The patch for the default monitoring mode for new items in the folder.
    /// </summary>
    public ScalarPatch<MonitoringMode?>? DefaultMonitoring { get; init; }

    /// <summary>
    /// The patch for the template for renamed files.
    /// </summary>
    public ScalarPatch<string?>? RenameTemplate { get; init; }

    /// <summary>
    /// The patch for the <see cref="UpgradeFileHandling"/> for replaced files.
    /// </summary>
    public ScalarPatch<UpgradeFileHandling>? UpgradeFileHandling { get; init; }

    /// <summary>
    /// The patch for whether discovery associates confident matches with existing library content.
    /// </summary>
    public ScalarPatch<bool>? AutoImport { get; init; }

    /// <summary>
    /// The patch for the default quality tier lists keyed by hierarchy level.
    /// </summary>
    public ScalarPatch<Dictionary<string, RootFolderDefault>>? Defaults { get; init; }
}
