using System;

namespace Magpie.Models.Data;

/// <summary>
/// A registered extension.
/// </summary>
public record Extension
{
    /// <summary>
    /// The extension id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The extension version.
    /// </summary>
    public required string Version { get; set; }

    /// <summary>
    /// The display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The target ABI version range the extension is compatible with.
    /// </summary>
    public string? TargetABI { get; set; }

    /// <summary>
    /// The file path to the extension assembly.
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// The fully qualified assembly name.
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
    /// The reason the extension is disabled, if applicable.
    /// </summary>
    public string? DisabledReason { get; set; }

    /// <summary>
    /// Loading order for the extension assembly.
    /// </summary>
    public int LoadOrder { get; set; }

    /// <summary>
    /// Whether the extension is managed via NuGet.
    /// </summary>
    public bool Managed { get; set; }

    /// <summary>
    /// The NuGet package identifier.
    /// </summary>
    public string? NuGetId { get; set; }

    /// <summary>
    /// The NuGet package repository URL.
    /// </summary>
    public string? PackageRepository { get; set; }

    /// <summary>
    /// Whether to automatically upgrade when new versions are available.
    /// </summary>
    public bool AutoUpgrade { get; set; }

    /// <summary>
    /// The date the extension was last checked for updates.
    /// </summary>
    public DateTime? LastChecked { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user that created the record.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// When the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user that last modified the record.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }
}
