using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Extensions;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing extensions.
/// </summary>
public interface IExtensionRepository :
    ICrudRepository<Guid, Extension>,
    IPatchRepository<Guid, Extension, ExtensionPatch>
{
    /// <summary>
    /// Get an extension by name.
    /// </summary>
    /// <param name="name">The extension name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<Extension?>> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get an extension by assembly name.
    /// </summary>
    /// <param name="assemblyName">The fully qualified assembly name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<Extension?>> GetByAssemblyNameAsync(string assemblyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all enabled extensions.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<Extension>>> GetEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all managed extensions (installed via NuGet).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<Extension>>> GetManagedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the last checked time for an extension.
    /// </summary>
    /// <param name="id">The extension identifier.</param>
    /// <param name="lastChecked">The time the extension was last checked for updates.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing <see langword="true"/> if the extension was updated,
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<Result<bool>> UpdateLastCheckedAsync(
        Guid id,
        DateTimeOffset lastChecked,
        CancellationToken cancellationToken = default);
}
