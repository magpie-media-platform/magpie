using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Data;

namespace Magpie.Sdk.Integrations;

/// <summary>
/// The integration contract for an external release indexer.
/// </summary>
public interface IIndexerIntegration
{
    /// <summary>
    /// The provider type slug.
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// The display name of the provider.
    /// </summary>
    string DisplayName => ProviderType;

    /// <summary>
    /// The schema of the indexer settings.
    /// </summary>
    IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; }

    /// <summary>
    /// The supported content type slugs.
    /// </summary>
    IReadOnlyList<string> SupportedContentTypes { get; }

    /// <summary>
    /// The combinations of search parameters that the indexer accepts together.
    /// </summary>
    IReadOnlyList<IReadOnlyCollection<string>> SearchFacets { get; }

    /// <summary>
    /// Whether the indexer accepts free-text keyword search.
    /// </summary>
    bool SupportsKeywordSearch { get; }

    /// <summary>
    /// Searches the indexer for releases.
    /// </summary>
    /// <param name="request">The criteria of the release search</param>
    /// <param name="settings">The settings of the configured provider</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The matching releases.</returns>
    Task<IEnumerable<Release>> SearchAsync(
        ReleaseSearchRequest request,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken);

    /// <summary>
    /// Tests the connection to the indexer.
    /// </summary>
    /// <param name="settings">The settings of the configured provider</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The result of the connection test.</returns>
    Task<ConfigurationTestResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a release payload with the session of the indexer.
    /// </summary>
    /// <param name="payloadUrl">The URL of the release payload</param>
    /// <param name="settings">The settings of the configured provider</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The response with the payload, or <see langword="null"/> when the payload needs no session</returns>
    Task<HttpResponseMessage?> FetchPayloadAsync(
        Uri payloadUrl,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<HttpResponseMessage?>(null);
}
