using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Models.Data;

/// <summary>
/// A top-level folder that holds imported content.
/// </summary>
public record RootFolder
{
    /// <summary>
    /// The unique id of the root folder.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The name of the folder to show.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The full path of the folder on disk.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// The slug of the content type that the folder holds.
    /// </summary>
    public required string ContentType { get; set; }

    /// <summary>
    /// The way an import places files in the folder, from <see cref="ImportMode"/>.
    /// </summary>
    public ImportMode ImportMode { get; set; } = ImportMode.Move;

    /// <summary>
    /// Whether the folder is the default for its content type.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// The default monitoring state for new items, or <see langword="null"/> to inherit.
    /// </summary>
    public bool? DefaultMonitoring { get; set; }

    /// <summary>
    /// The template for the names of imported files.
    /// </summary>
    public string? RenameTemplate { get; set; }

    /// <summary>
    /// The default action when an imported file already exists.
    /// </summary>
    public ImportCollisionMode DefaultImportCollisionMode { get; set; } = ImportCollisionMode.Fail;

    /// <summary>
    /// The action on the present file when an upgrade arrives, from <see cref="UpgradeFileHandling"/>.
    /// </summary>
    public UpgradeFileHandling UpgradeFileHandling { get; set; } = UpgradeFileHandling.Replace;

    /// <summary>
    /// The default quality tier lists keyed by hierarchy level.
    /// </summary>
    public Dictionary<string, RootFolderDefault> Defaults { get; set; } = [];

    /// <summary>
    /// The UTC time when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time when the record last changed.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last changed the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
