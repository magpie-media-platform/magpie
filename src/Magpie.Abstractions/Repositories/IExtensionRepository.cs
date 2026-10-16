using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Extensions;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for extensions.
/// </summary>
public interface IExtensionRepository :
    ICrudRepository<Guid, Extension>,
    IPatchRepository<Guid, Extension, ExtensionPatch>,
    IQueryRepository<Extension, ExtensionQuery>
{
    /// <summary>
    /// Registers the loaded extensions.
    /// </summary>
    /// <param name="discovered">The extensions that the host discovered</param>
    /// <param name="loaded">The loaded extensions</param>
    /// <param name="packageVersions">The package versions of the extensions, by extension id</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task RegisterLoadedExtensionsAsync(
        IReadOnlyList<Extension> discovered,
        IReadOnlyList<IExtension> loaded,
        IReadOnlyDictionary<Guid, string> packageVersions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the lifecycle version of an extension if it has the expected version.
    /// </summary>
    /// <param name="id">The extension id</param>
    /// <param name="expectedVersion">The expected version, or null for none</param>
    /// <param name="version">The new version, or null for none</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> if the version changed</returns>
    Task<bool> TrySetLifecycleVersionAsync(
        Guid id,
        string? expectedVersion,
        string? version,
        CancellationToken cancellationToken = default);
}
