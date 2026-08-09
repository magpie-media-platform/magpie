using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.IntegrationTestShims.Shared;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Integrations;

namespace Magpie.Extensions.IntegrationTestShims.Indexer;

internal sealed class MockIndexerIntegration : IIndexerIntegration
{
    public const string ProviderTypeName = "mock-indexer";
    public const string ApiUrlSettingKey = "apiUrl";
    public const string ApiKeySettingKey = "apiKey";

    private readonly ScenarioReader _scenarios;
    private readonly AuditWriter _audit;
    private readonly LatencyInjector _latency;

    public MockIndexerIntegration()
        : this(
            new ScenarioReader(ProviderTypeName),
            new AuditWriter(ProviderTypeName),
            new LatencyInjector(ProviderTypeName))
    {
    }

    internal MockIndexerIntegration(ScenarioReader scenarios, AuditWriter audit, LatencyInjector latency)
    {
        _scenarios = scenarios;
        _audit = audit;
        _latency = latency;
        SearchFacets =
        [
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "imdbid",
                "tmdbid",
                "tvdbid",
                "tvmazeid",
                "traktid",
            },
        ];
        SupportsKeywordSearch = true;
    }

    public string ProviderType => ProviderTypeName;

    public IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; } =
    [
        new()
        {
            Key = ApiUrlSettingKey,
            Type = DataType.String,
            DisplayName = "API URL",
            Description = "Base URL of the mock indexer endpoint.",
            Required = true,
            Placeholder = "https://mock.example.com",
        },
        new()
        {
            Key = ApiKeySettingKey,
            Type = DataType.String,
            DisplayName = "API Key",
            Description = "Credential used to authenticate against the mock indexer.",
            Required = true,
            Secret = true,
        },
    ];

    public IReadOnlyList<string> SupportedContentTypes { get; } = [];

    public IReadOnlyList<IReadOnlyCollection<string>> SearchFacets { get; }

    public bool SupportsKeywordSearch { get; }

    private string? ComputeSearchDiscriminator(ReleaseSearchRequest request)
    {
        var label = request.Title;
        if (string.IsNullOrWhiteSpace(label) && request.IdSearchParameter is { } parameter)
        {
            label = $"{parameter}-{request.IdSearchValue}";
        }

        return Slugify(label);
    }

    private IReadOnlyList<string> SettingsKeys(IReadOnlyDictionary<string, string> settings)
    {
        return [.. settings.Keys.OrderBy(k => k, StringComparer.Ordinal)];
    }

    private string? Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? null : slug;
    }

    public async Task<IEnumerable<Release>> SearchAsync(
        ReleaseSearchRequest request,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.IdSearchParameter is not null &&
            (string.IsNullOrWhiteSpace(request.IdSearchParameter) ||
                string.IsNullOrWhiteSpace(request.IdSearchValue)))
        {
            return [];
        }

        const string method = "search";
        await _latency.DelayAsync(method, cancellationToken).ConfigureAwait(false);

        var discriminator = ComputeSearchDiscriminator(request);
        var response = await _scenarios
            .ReadAsync<SearchScenario>(method, discriminator, cancellationToken)
            .ConfigureAwait(false);

        var releases = response.Releases ?? [];
        foreach (var release in releases)
        {
            release.Id = Guid.NewGuid();
        }

        await _audit.WriteAsync(method, new { search = request, settings }, releases, cancellationToken)
            .ConfigureAwait(false);

        return releases;
    }

    public async Task<ConfigurationTestResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        const string method = "testConnection";
        await _latency.DelayAsync(method, cancellationToken).ConfigureAwait(false);

        var response = await _scenarios
            .ReadAsync<ConfigurationTestResult>(method, discriminator: null, cancellationToken)
            .ConfigureAwait(false);

        await _audit
            .WriteAsync(method, new { settingsKeys = SettingsKeys(settings) }, response, cancellationToken)
            .ConfigureAwait(false);

        return response;
    }

    internal sealed class SearchScenario
    {
        public List<Release> Releases { get; set; } = [];
    }
}
