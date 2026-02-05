using System.Collections.Generic;
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
}
