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
}
