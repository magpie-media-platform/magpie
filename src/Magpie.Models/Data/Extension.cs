using System;

namespace Magpie.Models.Data;

/// <summary>
/// A registered extension.
/// </summary>
public record Extension
{
    /// <summary>
    /// The unique id of the extension.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The version of the extension.
    /// </summary>
    public required string Version { get; set; }

    /// <summary>
    /// The version of the last run of the install and upgrade hooks, or null before the first run.
    /// </summary>
    public string? LifecycleVersion { get; set; }

    /// <summary>
    /// The package id of the extension.
    /// </summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>
    /// The display name of the extension.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The author of the extension.
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// The description of the extension.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The extension ABI version that the extension targets.
    /// </summary>
    public string? TargetABI { get; set; }

    /// <summary>
    /// The file path of the extension assembly.
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// The name of the extension assembly.
    /// </summary>
    public required string AssemblyName { get; set; }

    /// <summary>
    /// Whether the extension is installed.
    /// </summary>
    public bool Installed { get; set; }

    /// <summary>
    /// Whether the extension is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The reason the extension is disabled.
    /// </summary>
    public string? DisabledReason { get; set; }

    /// <summary>
    /// The position in the load order.
    /// </summary>
    public int LoadOrder { get; set; }

    /// <summary>
    /// Whether Magpie manages the extension package.
    /// </summary>
    public bool Managed { get; set; }

    /// <summary>
    /// The NuGet package id.
    /// </summary>
    public string? NuGetId { get; set; }

    /// <summary>
    /// The URL of the package repository.
    /// </summary>
    public string? PackageRepository { get; set; }

    /// <summary>
    /// Whether the extension upgrades automatically.
    /// </summary>
    public bool AutoUpgrade { get; set; }

    /// <summary>
    /// The UTC time of the last check for a new version.
    /// </summary>
    public DateTime? LastChecked { get; set; }

    /// <summary>
    /// The UTC time the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user who created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// The UTC time the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user who last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
