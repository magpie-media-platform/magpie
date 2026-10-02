using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.IntegrationTestShims.Shared;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Sdk.Integrations;

namespace Magpie.Extensions.IntegrationTestShims.DownloadClient;

internal sealed class MockDownloadClientIntegration :
    IDownloadClientIntegration,
    IPausableDownloadClient,
    IResumableDownloadClient,
    IForcableDownloadClient
{
    private const string DownloadsRootEnvironmentVariable = "MAGPIE_TEST_DOWNLOADS_ROOT";
    private const string MediaFixturesRootEnvironmentVariable = "MAGPIE_TEST_MEDIA_FIXTURES_ROOT";

    private static readonly string[] SupportedProtocolsArray = ["torrent", "usenet"];
    public const string ProviderTypeName = "mock-download-client";

    private readonly ScenarioReader _scenarioReader = new(ProviderTypeName);
    private readonly AuditWriter _auditWriter = new(ProviderTypeName);
    private readonly LatencyInjector _latencyInjector = new(ProviderTypeName);
    private readonly ConcurrentDictionary<string, int> _statusCursors = [];

    public string ProviderType => ProviderTypeName;

    public IEnumerable<string> SupportedProtocols => SupportedProtocolsArray;

    public IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; } =
    [
        new()
        {
            Key = "host",
            Type = DataType.String,
            DisplayName = "Host",
            Description = "Hostname or IP of the mock download client.",
            Required = true,
            Placeholder = "localhost",
        },
        new()
        {
            Key = "port",
            Type = DataType.Number,
            DisplayName = "Port",
            Description = "TCP port for the mock download client.",
            DefaultValue = "8080",
        },
        new()
        {
            Key = "apiKey",
            Type = DataType.String,
            DisplayName = "API Key",
            Secret = true,
            Required = true,
            Description = "API key used to authenticate with the mock download client.",
        },
    ];

    private static string ResolveExternalId(string? scenarioExternalId, string? releaseExternalId)
    {
        if (!string.IsNullOrWhiteSpace(scenarioExternalId))
        {
            return scenarioExternalId;
        }

        return string.IsNullOrWhiteSpace(releaseExternalId)
            ? Guid.NewGuid().ToString("N")
            : releaseExternalId;
    }

    private static string? LandedFolder(string externalId)
    {
        var downloadsRoot = ReadOptionalEnvironmentPath(DownloadsRootEnvironmentVariable);
        return downloadsRoot is null
            ? null
            : Path.Combine(downloadsRoot, SanitizeSegment(externalId));
    }

    private static string? LandFiles(string externalId, IReadOnlyList<ScenarioLandedFile>? land)
    {
        if (land is null || land.Count == 0)
        {
            return null;
        }

        var targetRoot = LandedFolder(externalId);
        if (targetRoot is null)
        {
            return null;
        }

        Directory.CreateDirectory(targetRoot);
        var rootPrefix = targetRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var file in land)
        {
            if (string.IsNullOrWhiteSpace(file.Source) || string.IsNullOrWhiteSpace(file.Target))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(targetRoot, file.Target));
            if (!destination.StartsWith(rootPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Landed file target {file.Target} escapes the download folder {targetRoot}.");
            }

            var source = ResolveSourcePath(file.Source);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException($"Media fixture {file.Source} does not exist at {source}.", source);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(source, destination, overwrite: true);
        }

        return targetRoot;
    }

    private static string ResolveSourcePath(string source)
    {
        if (Path.IsPathRooted(source))
        {
            return source;
        }

        var mediaRoot = ReadOptionalEnvironmentPath(MediaFixturesRootEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"Relative media fixture {source} requires {MediaFixturesRootEnvironmentVariable}.");
        return Path.Combine(mediaRoot, source);
    }

    private static string? ReadOptionalEnvironmentPath(string environmentVariable)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string SanitizeSegment(string value)
    {
        var buffer = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            buffer[i] = char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_';
        }

        return new string(buffer);
    }

    public async Task<DownloadInfo> DownloadReleaseAsync(
        Release release,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        const string method = "DownloadReleaseAsync";
        await _latencyInjector.DelayAsync(method, cancellationToken).ConfigureAwait(false);
        var scenario = await _scenarioReader
            .ReadAsync<DownloadReleaseScenario>(
                method,
                discriminator: release.ExternalId,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var externalId = ResolveExternalId(scenario.ExternalId, release.ExternalId);

        var response = new DownloadInfo
        {
            ExternalId = externalId,
            Status = scenario.Status,
            StatusReason = scenario.StatusReason,
            OutputPath = scenario.OutputPath,
            ProgressPercent = scenario.ProgressPercent,
            HeldByPolicy = scenario.HeldByPolicy,
        };

        var landedPath = LandFiles(externalId, scenario.Land);
        if (landedPath is not null)
        {
            response = response with { OutputPath = landedPath };
        }

        await _auditWriter
            .WriteAsync(
                method,
                new
                {
                    releaseId = release.Id,
                    title = release.Title,
                    protocol = release.Protocol,
                    infoUrl = release.InfoUrl?.ToString(),
                    settings,
                    externalId,
                    landedPath,
                    landedFiles = scenario.Land?.Select(f => f.Target).ToArray() ?? [],
                },
                response,
                cancellationToken)
            .ConfigureAwait(false);

        return response;
    }

    public async Task<DownloadInfo> GetDownloadAsync(
        string externalId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(externalId))
        {
            throw new ArgumentException("An external ID is required.", nameof(externalId));
        }

        const string method = "GetDownloadAsync";
        await _latencyInjector.DelayAsync(method, cancellationToken).ConfigureAwait(false);

        var states = await _scenarioReader
            .ReadAsync<List<DownloadInfo>>(
                "status",
                discriminator: externalId,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (states.Count == 0)
        {
            throw new InvalidOperationException(
                $"Scenario file 'status-{externalId}.json' for plugin {ProviderTypeName} " +
                "deserialized to an empty array.");
        }

        var index = _statusCursors.AddOrUpdate(
            externalId,
            addValueFactory: _ => 0,
            updateValueFactory: (_, current) => current + 1);
        if (index >= states.Count)
        {
            index = states.Count - 1;
        }

        var response = states[index];

        var landedFolder = LandedFolder(externalId);
        if (landedFolder is not null && Directory.Exists(landedFolder))
        {
            response = response with { OutputPath = landedFolder };
        }

        await _auditWriter
            .WriteAsync(method, new { externalId, settings, index }, response, cancellationToken)
            .ConfigureAwait(false);

        return response;
    }

    public async Task<bool> RemoveDownloadAsync(
        string downloadId,
        bool deleteFiles,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(downloadId))
        {
            throw new ArgumentException("A download ID is required.", nameof(downloadId));
        }

        const string method = "RemoveDownloadAsync";
        await _latencyInjector.DelayAsync(method, cancellationToken).ConfigureAwait(false);
        var response = await _scenarioReader
            .ReadAsync<bool>(method, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _auditWriter
            .WriteAsync(method, new { downloadId, deleteFiles, settings }, response, cancellationToken)
            .ConfigureAwait(false);

        if (deleteFiles && LandedFolder(downloadId) is { } landedFolder && Directory.Exists(landedFolder))
        {
            Directory.Delete(landedFolder, recursive: true);
        }

        return response;
    }

    public async Task<bool> PauseDownloadAsync(
        string externalId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        return await ReadControlResponseAsync(
            "PauseDownloadAsync",
            externalId,
            settings,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ResumeDownloadAsync(
        string externalId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        return await ReadControlResponseAsync(
            "ResumeDownloadAsync",
            externalId,
            settings,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ForceStartDownloadAsync(
        string externalId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        return await ReadControlResponseAsync(
            "ForceStartDownloadAsync",
            externalId,
            settings,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConfigurationTestResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        const string method = "TestConnectionAsync";
        await _latencyInjector.DelayAsync(method, cancellationToken).ConfigureAwait(false);
        var scenario = await _scenarioReader
            .ReadAsync<TestConnectionScenario>(method, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var settingsMatch = scenario.ExpectedSettings.Count == 0 ||
            scenario.ExpectedSettings.All(expected =>
                settings.TryGetValue(expected.Key, out var actual) &&
                string.Equals(actual, expected.Value, StringComparison.Ordinal));
        var response = new ConfigurationTestResult
        {
            Success = scenario.Success && settingsMatch,
            Message = settingsMatch ? scenario.Message : "Settings did not match the configured fixture.",
            FailureKind = settingsMatch ? scenario.FailureKind : ConfigurationFailureKind.Auth,
        };

        await _auditWriter
            .WriteAsync(method, new { settings }, response, cancellationToken)
            .ConfigureAwait(false);

        return response;
    }

    private async Task<bool> ReadControlResponseAsync(
        string method,
        string externalId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(externalId))
        {
            throw new ArgumentException("An external ID is required.", nameof(externalId));
        }

        await _latencyInjector.DelayAsync(method, cancellationToken).ConfigureAwait(false);
        var response = await _scenarioReader
            .ReadAsync<bool>(method, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _auditWriter
            .WriteAsync(method, new { externalId, settings }, response, cancellationToken)
            .ConfigureAwait(false);

        return response;
    }

    internal sealed class DownloadReleaseScenario
    {
        public string? ExternalId { get; set; }

        public DownloadStatus Status { get; set; }

        public string? StatusReason { get; set; }

        public string? OutputPath { get; set; }

        public decimal? ProgressPercent { get; set; }

        public bool HeldByPolicy { get; set; }

        public List<ScenarioLandedFile>? Land { get; set; }
    }

    internal sealed class ScenarioLandedFile
    {
        public string Source { get; set; } = string.Empty;

        public string Target { get; set; } = string.Empty;
    }

    internal sealed class TestConnectionScenario
    {
        public bool Success { get; set; }

        public string Message { get; set; } = string.Empty;

        public ConfigurationFailureKind FailureKind { get; set; }

        public Dictionary<string, string> ExpectedSettings { get; set; } = [];
    }
}
