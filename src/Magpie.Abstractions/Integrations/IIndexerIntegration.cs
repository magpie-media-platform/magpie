using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Indexers;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Integrations;

/// <summary>
/// Integration with an external release indexer.
/// </summary>
public interface IIndexerIntegration
{
    /// <summary>
    /// Gets the slug of the type of the indexer integration.
    /// </summary>
    string IndexerType { get; }

    /// <summary>
    /// Searches for releases matching the given criteria.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="categories">Optional category filters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result containing a list of matching releases on success, or an error on failure.</returns>
    Task<Result<IReadOnlyList<TrackedRelease>>> SearchAsync(
        string query,
        IReadOnlyList<string>? categories,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Tests the connection to the indexer.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result indicating success or failure with an error reason.</returns>
    Task<Result<bool>> TestConnectionAsync(CancellationToken cancellationToken);
}
