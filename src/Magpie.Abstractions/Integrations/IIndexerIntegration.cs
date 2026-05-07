using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Integrations;

/// <summary>
/// Integration with an external release indexer.
/// </summary>
public interface IIndexerIntegration
{
    /// <summary>
    /// Get the slug of the type of the indexer integration.
    /// </summary>
    string IndexerType { get; }

    /// <summary>
    /// Searches for releases matching the given criteria.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="categories">Optional category filters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching releases. Empty when no matches are found; implementations should not return <see langword="null"/>.</returns>
    Task<IEnumerable<Release>> SearchAsync(
        string query,
        IEnumerable<string>? categories,
        CancellationToken cancellationToken);

    /// <summary>
    /// Tests the connection to the indexer.
    /// </summary>
    /// <param name="settings">Provider-specific configuration to use for the test (api url, api key, etc.).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="ConnectionTestResult"/> describing success or failure with a message.</returns>
    Task<ConnectionTestResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);
}
