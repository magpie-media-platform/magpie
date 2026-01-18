using System;

namespace Magpie.Models.Extensions;

/// <summary>
/// A registered extension.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Version">The extension version.</param>
/// <param name="Name">The display name.</param>
/// <param name="TargetABI">The target ABI version range the extension is compatible with.</param>
/// <param name="FilePath">The file path to the extension assembly.</param>
/// <param name="AssemblyName">The fully qualified assembly name.</param>
/// <param name="Enabled">Whether the extension is enabled.</param>
/// <param name="DisabledReason">The reason the extension is disabled, if applicable.</param>
/// <param name="LoadOrder">Higher values load later and win in DI registration conflicts.</param>
/// <param name="Managed">Whether the extension is managed via NuGet.</param>
/// <param name="NuGetId">The NuGet package identifier.</param>
/// <param name="PackageRepository">The NuGet package repository URL.</param>
/// <param name="AutoUpgrade">Whether to automatically upgrade when new versions are available.</param>
/// <param name="LastChecked">The date the extension was last checked for updates.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record Extension(
    Guid Id,
    string Version,
    string Name,
    string? TargetABI,
    string FilePath,
    string AssemblyName,
    bool Enabled,
    string? DisabledReason,
    int LoadOrder,
    bool Managed,
    string? NuGetId,
    string? PackageRepository,
    bool AutoUpgrade,
    DateTimeOffset? LastChecked,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
