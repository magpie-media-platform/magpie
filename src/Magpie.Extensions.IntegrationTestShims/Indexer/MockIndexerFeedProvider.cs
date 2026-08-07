using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.IntegrationTestShims.Shared;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Integrations;

using IndexerModel = Magpie.Models.Data.Indexer;

namespace Magpie.Extensions.IntegrationTestShims.Indexer;

internal sealed class MockIndexerFeedProvider : IIndexerFeedProvider
{
    public const string ProviderTypeName = "mock-indexer-feed";

    private readonly ScenarioReader _scenarios = new(ProviderTypeName);
    private readonly AuditWriter _audit = new(ProviderTypeName);
    private readonly LatencyInjector _latency = new(ProviderTypeName);

    public string ProviderType => ProviderTypeName;

    public IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; } =
    [
        new SettingSchemaDefinition
        {
            Key = "endpoint",
            Type = DataType.String,
            DisplayName = "Endpoint",
            Required = true,
        },
        new SettingSchemaDefinition
        {
            Key = "apiKey",
            Type = DataType.String,
            DisplayName = "API Key",
            Required = true,
            Secret = true,
        },
    ];

    public async Task<IEnumerable<IndexerModel>> SyncAsync(
        IndexerFeed feed,
        CancellationToken cancellationToken = default)
    {
        const string method = nameof(SyncAsync);
        await _latency.DelayAsync(method, cancellationToken).ConfigureAwait(false);
        var response = await _scenarios
            .ReadAsync<List<IndexerModel>>(method, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await _audit.WriteAsync(method, feed, response, cancellationToken).ConfigureAwait(false);
        return response;
    }

    public async Task<bool> TestConnectionAsync(
        IndexerFeed feed,
        CancellationToken cancellationToken = default)
    {
        const string method = nameof(TestConnectionAsync);
        await _latency.DelayAsync(method, cancellationToken).ConfigureAwait(false);
        var response = await _scenarios
            .ReadAsync<bool>(method, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await _audit.WriteAsync(method, feed, response, cancellationToken).ConfigureAwait(false);
        return response;
    }
}
