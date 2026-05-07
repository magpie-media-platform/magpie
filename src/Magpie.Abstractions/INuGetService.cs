using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Abstractions;

/// <summary>
/// Service for working with NuGet.
/// </summary>
public interface INuGetService
{
    /// <summary>
    /// Search for NuGet packages matching the query and configured tags.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of matching packages.</returns>
    Task<IEnumerable<NuGetSearchResult>> SearchAsync(string? query, CancellationToken cancellationToken);

    /// <summary>
    /// Download a NuGet package as a .nupkg stream.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="version">
    /// The version to download. When null the implementation should resolve to the latest stable
    /// version of the package.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A seekable stream containing the .nupkg bytes, or <see langword="null"/> if the package
    /// cannot be located.
    /// </returns>
    Task<Stream?> DownloadPackageAsync(string packageId, string? version, CancellationToken cancellationToken);

    /// <summary>
    /// List the versions of a NuGet package known to the configured feed.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The published versions, ordered ascending. An empty list when the package is unknown.</returns>
    Task<IReadOnlyList<string>> GetVersionsAsync(string packageId, CancellationToken cancellationToken);
}
