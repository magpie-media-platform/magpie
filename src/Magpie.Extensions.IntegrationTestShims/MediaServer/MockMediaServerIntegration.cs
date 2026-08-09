using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.IntegrationTestShims.Shared;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Enums;
using Magpie.Sdk.Integrations;

using MediaServerEntity = Magpie.Models.Data.MediaServer;

namespace Magpie.Extensions.IntegrationTestShims.MediaServer;

internal sealed class MockMediaServerIntegration :
    IMediaServerIntegration,
    IRefreshableMediaServerIntegration,
    ILinkableMediaServerIntegration
{
    public const string ProviderTypeName = "mock-media-server";

    private const string DeepLinkBase = "https://mock-media-server.local/item";

    private readonly AuditWriter _auditWriter;
    private readonly LatencyInjector _latencyInjector;
    private readonly ScenarioReader _scenarioReader;

    public MockMediaServerIntegration()
        : this(
            new AuditWriter(ProviderTypeName),
            new LatencyInjector(ProviderTypeName),
            new ScenarioReader(ProviderTypeName))
    {
    }

    internal MockMediaServerIntegration(
        AuditWriter auditWriter,
        LatencyInjector latencyInjector,
        ScenarioReader scenarioReader)
    {
        _auditWriter = auditWriter;
        _latencyInjector = latencyInjector;
        _scenarioReader = scenarioReader;
    }

    public string ProviderType => ProviderTypeName;

    public IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; } =
    [
        new()
        {
            Key = "host",
            Type = DataType.String,
            DisplayName = "Host",
            Description = "Hostname or base URL of the mock media server.",
            Required = true,
            Placeholder = "localhost",
        },
        new()
        {
            Key = "port",
            Type = DataType.Number,
            DisplayName = "Port",
            Description = "TCP port the mock server listens on.",
            Required = false,
            DefaultValue = "8096",
            Placeholder = "8096",
        },
        new()
        {
            Key = "apiKey",
            Type = DataType.String,
            DisplayName = "API Key",
            Secret = true,
            Description = "Opaque token recorded into the audit log for assertions.",
            Required = true,
        },
    ];

    private static string EncodeExternalIds(IReadOnlyDictionary<string, string> externalIds)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var pair in externalIds)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            pairs.Add(pair);
        }

        if (pairs.Count == 0)
        {
            return string.Empty;
        }

        pairs.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key));

        var builder = new StringBuilder();
        for (var i = 0; i < pairs.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Uri.EscapeDataString(pairs[i].Key));
            builder.Append(':');
            builder.Append(Uri.EscapeDataString(pairs[i].Value));
        }

        return builder.ToString();
    }

    public async Task<ConfigurationTestResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        const string method = nameof(TestConnectionAsync);
        await _latencyInjector.DelayAsync(method, cancellationToken).ConfigureAwait(false);

        var response = await _scenarioReader
            .ReadAsync<ConfigurationTestResult>(method, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _auditWriter
            .WriteAsync(method, new TestConnectionRequest { Settings = settings }, response, cancellationToken)
            .ConfigureAwait(false);

        return response;
    }

    public async Task<bool> RefreshForContentAsync(
        MediaServerEntity server,
        IReadOnlyDictionary<string, string> contentExternalIds,
        string contentType,
        string rootFolderPath,
        CancellationToken cancellationToken = default)
    {
        const string method = nameof(RefreshForContentAsync);
        await _latencyInjector.DelayAsync(method, cancellationToken).ConfigureAwait(false);

        var response = await _scenarioReader
            .ReadAsync<RefreshForContentResponse>(method, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _auditWriter
            .WriteAsync(
                method,
                new RefreshForContentRequest
                {
                    ServerId = server.Id,
                    ContentExternalIds = contentExternalIds,
                    ContentType = contentType,
                    RootFolderPath = rootFolderPath,
                },
                response,
                cancellationToken)
            .ConfigureAwait(false);

        return response.Refreshed;
    }

    public async Task<Uri?> BuildItemDeepLinkAsync(
        MediaServerEntity server,
        IReadOnlyDictionary<string, string> externalIds,
        string contentType,
        string rootFolderPath,
        string title,
        CancellationToken cancellationToken = default)
    {
        const string method = nameof(BuildItemDeepLinkAsync);
        var discriminator = externalIds
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Value)
            .FirstOrDefault();
        DeepLinkResponse? response;
        try
        {
            response = await _scenarioReader
                .ReadAsync<DeepLinkResponse>(method, discriminator, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ScenarioFileMissingException)
        {
            response = null;
        }

        if (response is not null)
        {
            return Uri.TryCreate(response.Url, UriKind.Absolute, out var overridden) ? overridden : null;
        }

        var encoded = EncodeExternalIds(externalIds);
        if (string.IsNullOrEmpty(encoded))
        {
            return null;
        }

        return Uri.TryCreate($"{DeepLinkBase}?ids={encoded}", UriKind.Absolute, out var parsed)
            ? parsed
            : null;
    }

    internal sealed class TestConnectionRequest
    {
        public IReadOnlyDictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();
    }

    internal sealed class RefreshForContentRequest
    {
        public Guid ServerId { get; set; }

        public IReadOnlyDictionary<string, string> ContentExternalIds { get; set; }
            = new Dictionary<string, string>();

        public string ContentType { get; set; } = string.Empty;

        public string RootFolderPath { get; set; } = string.Empty;
    }

    internal sealed class RefreshForContentResponse
    {
        public bool Refreshed { get; set; }
    }

    internal sealed class DeepLinkResponse
    {
        public string? Url { get; set; }
    }
}
