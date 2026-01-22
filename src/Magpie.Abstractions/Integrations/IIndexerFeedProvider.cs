using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Indexers;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Integrations;

/// <summary>
/// Provider for discovering indexer definitions from a feed source.
/// </summary>
public interface IIndexerFeedProvider
{
    /// <summary>
    /// The slug of the indexer feed provider type.
    /// </summary>
    string IndexerFeedProviderType { get; }

    /// <summary>
    /// Syncs indexer definitions from the feed.
    /// </summary>
    /// <param name="feed">The feed configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result containing the discovered definitions on success, or an error on failure.</returns>
    Task<Result<IReadOnlyList<IndexerDefinition>>> SyncAsync(
        IndexerFeed feed,
        CancellationToken cancellationToken);

    /// <summary>
    /// Tests the connection to the feed source.
    /// </summary>
    /// <param name="feed">The feed configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result indicating success or failure with an error reason.</returns>
    Task<Result<bool>> TestConnectionAsync(
        IndexerFeed feed,
        CancellationToken cancellationToken);
}
