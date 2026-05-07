using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Abstractions;

/// <summary>
/// Coordinates the disk and database side of installing, updating, and removing extensions.
/// </summary>
/// <remarks>
/// <para>
/// Implementations stage assemblies under the configured extensions directory and register
/// metadata in <see cref="Repositories.IExtensionRepository"/>. They do not load assemblies
/// into the host process: extensions are bound to the default <c>AssemblyLoadContext</c>
/// when the host starts and cannot be unloaded mid-process. Install / update / uninstall
/// take effect on the next host restart.
/// </para>
/// </remarks>
public interface IExtensionInstaller
{
    /// <summary>
    /// Install an extension by downloading a NuGet package from the configured feed.
    /// </summary>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="version">The version to install. When null the latest stable version is used.</param>
    /// <param name="modifiedByUserId">The id of the user requesting the install.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly registered extension entity.</returns>
    Task<Extension> InstallFromNuGetAsync(
        string packageId,
        string? version,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Install an extension from a .nupkg or .zip stream.
    /// </summary>
    /// <param name="packageStream">The package contents.</param>
    /// <param name="suggestedName">A name hint -- typically the upload's file name -- used as the directory name.</param>
    /// <param name="modifiedByUserId">The id of the user uploading the package.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly registered extension entity.</returns>
    Task<Extension> InstallFromStreamAsync(
        Stream packageStream,
        string suggestedName,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Check the configured NuGet feed for newer versions of every managed extension.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One <see cref="ExtensionUpdateInfo"/> per managed extension.</returns>
    Task<IReadOnlyList<ExtensionUpdateInfo>> CheckUpdatesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Update an installed managed extension to a newer version.
    /// </summary>
    /// <param name="extensionId">The id of the extension to update.</param>
    /// <param name="version">The version to install. When null the latest stable version is used.</param>
    /// <param name="modifiedByUserId">The id of the user requesting the update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated extension entity, or <see langword="null"/> if the extension does not exist.</returns>
    Task<Extension?> UpdateAsync(
        Guid extensionId,
        string? version,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Remove an extension's directory, registry row, and install-record entry.
    /// </summary>
    /// <param name="extensionId">The id of the extension to remove.</param>
    /// <param name="modifiedByUserId">The id of the user requesting the uninstall.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the extension was removed, otherwise <see langword="false"/>.</returns>
    Task<bool> UninstallAsync(
        Guid extensionId,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken);
}

/// <summary>
/// The result of checking whether an installed extension has an available update.
/// </summary>
/// <param name="ExtensionId">The id of the installed extension.</param>
/// <param name="PackageId">The NuGet package identifier of the extension, when known.</param>
/// <param name="CurrentVersion">The currently installed version.</param>
/// <param name="LatestVersion">The latest version available on the feed, or <see langword="null"/> when unknown.</param>
/// <param name="HasUpdate"><see langword="true"/> when <paramref name="LatestVersion"/> is newer than <paramref name="CurrentVersion"/>.</param>
public sealed record ExtensionUpdateInfo(
    Guid ExtensionId,
    string? PackageId,
    string CurrentVersion,
    string? LatestVersion,
    bool HasUpdate);
