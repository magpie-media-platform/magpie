using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Data;

namespace Magpie.Sdk.Integrations;

/// <summary>
/// A provider of indexers from an external feed.
/// </summary>
public interface IIndexerFeedProvider
{
    /// <summary>
    /// The provider type slug.
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// The schema of the provider settings.
    /// </summary>
    IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; }

    /// <summary>
    /// Gets the indexers that a feed offers.
    /// </summary>
    /// <param name="feed">The feed to read</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The indexers of the feed.</returns>
    Task<IEnumerable<Indexer>> SyncAsync(
        IndexerFeed feed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the connection to a feed.
    /// </summary>
    /// <param name="feed">The feed to test</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> for a successful connection, otherwise <see langword="false"/>.</returns>
    Task<bool> TestConnectionAsync(
        IndexerFeed feed,
        CancellationToken cancellationToken = default);
}
