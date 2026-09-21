using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for extension package management.
/// </summary>
public interface IExtensionService
{
    /// <summary>
    /// Lists the installed extensions and the updates available for them.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<ExtensionListItem>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// An extension by id.
    /// </summary>
    /// <param name="extensionId">The id of the extension to return</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Extension?> GetAsync(Guid extensionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs an extension from a configured package source.
    /// </summary>
    /// <param name="packageId">The id of the package to install</param>
    /// <param name="version">The version to install, or <see langword="null"/> for the latest</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ExtensionInstallResult> InstallFromSourceAsync(
        string packageId,
        string? version,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs an extension from an uploaded package.
    /// </summary>
    /// <param name="package">The stream that holds the package content</param>
    /// <param name="fileName">The name of the uploaded file</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ExtensionInstallResult> UploadAsync(
        Stream package,
        string fileName,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upgrades an extension to the latest available version.
    /// </summary>
    /// <param name="extensionId">The id of the extension to upgrade</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ExtensionInstallResult?> UpgradeToLatestAsync(
        Guid extensionId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upgrades an extension to one version.
    /// </summary>
    /// <param name="extensionId">The id of the extension to upgrade</param>
    /// <param name="version">The version to install</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ExtensionInstallResult?> UpgradeToVersionAsync(
        Guid extensionId,
        string version,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables an extension.
    /// </summary>
    /// <param name="extensionId">The id of the extension to enable</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Extension?> EnableAsync(
        Guid extensionId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables an extension.
    /// </summary>
    /// <param name="extensionId">The id of the extension to disable</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<Extension?> DisableAsync(
        Guid extensionId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches an extension.
    /// </summary>
    /// <param name="extensionId">The id of the extension to patch</param>
    /// <param name="patch">The fields to change, including the load order ordinal</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchAsync(
        Guid extensionId,
        ExtensionPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uninstalls an extension.
    /// </summary>
    /// <param name="extensionId">The id of the extension to uninstall</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> UninstallAsync(
        Guid extensionId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches the configured package sources for extensions.
    /// </summary>
    /// <param name="query">The search text, or <see langword="null"/> for every package</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<NuGetSearchResult>> SearchAsync(
        string? query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the extension package sources.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<ExtensionPackageSource>> ListPackageSourcesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// An extension package source by id.
    /// </summary>
    /// <param name="packageSourceId">The id of the package source to return</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ExtensionPackageSource?> GetPackageSourceAsync(
        Guid packageSourceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an extension package source.
    /// </summary>
    /// <param name="create">The values for the new package source</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ExtensionPackageSource> CreatePackageSourceAsync(
        ExtensionPackageSourceCreate create,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches an extension package source.
    /// </summary>
    /// <param name="packageSourceId">The id of the package source to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchPackageSourceAsync(
        Guid packageSourceId,
        ExtensionPackageSourcePatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an extension package source.
    /// </summary>
    /// <param name="packageSourceId">The id of the package source to delete</param>
    /// <param name="actingUserId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeletePackageSourceAsync(
        Guid packageSourceId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default);
}
