using System;

namespace Magpie.Models;

/// <summary>
/// The update availability of an installed extension.
/// </summary>
/// <param name="ExtensionId">The id of the extension</param>
/// <param name="PackageId">The id of the package, when known</param>
/// <param name="CurrentVersion">The version that is installed now</param>
/// <param name="LatestVersion">The latest available version, when known</param>
/// <param name="HasUpdate">Whether an update is available</param>
public sealed record ExtensionUpdateInfo(
    Guid ExtensionId,
    string? PackageId,
    string CurrentVersion,
    string? LatestVersion,
    bool HasUpdate);
