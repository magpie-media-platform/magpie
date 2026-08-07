using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Sdk.Integrations;

using Microsoft.Extensions.Logging;

using MonoTorrent;

namespace Magpie.Extensions.Qbittorrent;

internal sealed class QbittorrentDownloadClientIntegration(
    ILogger<QbittorrentDownloadClientIntegration> logger,
    IQbittorrentClient client,
    IHttpClientFactory httpClientFactory) :
    IDownloadClientIntegration,
    IPausableDownloadClient,
    IResumableDownloadClient,
    IForcableDownloadClient,
    IValidatesIntegrationSettings,
    IDisposable
{
    private static readonly string[] SupportedProtocolsArray = ["torrent"];
    public const string ProviderTypeId = "qbittorrent";
    public const string TorrentFileHttpClientName = "magpie.qbittorrent.torrentfile";

    private readonly ILogger<QbittorrentDownloadClientIntegration> _logger = logger;

    private readonly IQbittorrentClient _client = client;

    private readonly HttpClient _torrentDownloadHttpClient = httpClientFactory.CreateClient(TorrentFileHttpClientName);

    public string ProviderType => ProviderTypeId;

    public IEnumerable<string> SupportedProtocols => SupportedProtocolsArray;

    public IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; } =
    [
        new()
        {
            Key = "host",
            Type = DataType.String,
            DisplayName = "Host",
            Description = "Base URL of the qBittorrent WebUI (e.g. http://localhost:8080).",
            Required = true,
            Placeholder = "http://localhost:8080",
        },
        new()
        {
            Key = "username",
            Type = DataType.String,
            DisplayName = "Username",
            Description = "Optional. Required when the WebUI enforces authentication.",
        },
        new()
        {
            Key = "password",
            Type = DataType.String,
            DisplayName = "Password",
            Secret = true,
            Description = "Optional. Required when the WebUI enforces authentication.",
        },
        new()
        {
            Key = "category",
            Type = DataType.String,
            DisplayName = "Category",
            DefaultValue = QbittorrentSettings.DefaultCategory,
            Description = "qBittorrent category applied to added torrents. Defaults to 'magpie'.",
        },
        new()
        {
            Key = "paused",
            Type = DataType.Boolean,
            DisplayName = "Add as paused",
            DefaultValue = "false",
            Description = "When true, torrents are added to qBittorrent in a paused state.",
        },
        new()
        {
            Key = DownloadClientSettingsKeys.HonorClientHoldPolicy,
            Type = DataType.Boolean,
            DisplayName = "Honor client hold policy",
            DefaultValue = "true",
            Description = "When true, cleanup waits until qBittorrent releases imported torrents.",
        },
    ];

    private static string InferTorrentFileName(Uri torrentUrl)
    {
        var segments = torrentUrl.Segments;
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            var segment = segments[i].TrimEnd('/');
            if (segment.Length > 0)
            {
                return Uri.UnescapeDataString(segment);
            }
        }

        return "release.torrent";
    }

    private static string? ResolveOutputPath(TorrentInfo torrent)
        => !string.IsNullOrEmpty(torrent.ContentPath)
            ? torrent.ContentPath
            : string.IsNullOrEmpty(torrent.SavePath) ? null : torrent.SavePath;

    private static async Task<TorrentInfo?> FindTorrentByHashAsync(
        IQbittorrentClient client,
        QbittorrentSettings settings,
        string hash,
        CancellationToken cancellationToken)
    {
        var torrents = await client.GetTorrentInfoAsync(settings, hash, cancellationToken)
            .ConfigureAwait(false);
        foreach (var torrent in torrents)
        {
            if (string.Equals(torrent.Hash, hash, StringComparison.OrdinalIgnoreCase))
            {
                return torrent;
            }
        }

        return null;
    }

    private static DownloadInfo ProjectDuplicateMagnet(string externalId, TorrentInfo torrent)
    {
        var info = ProjectTorrent(externalId, torrent);
        return info.Status is DownloadStatus.Failed
            ? info with { Status = DownloadStatus.Grabbed, StatusReason = null }
            : info;
    }

    private static DownloadInfo ProjectTorrent(string externalId, TorrentInfo torrent)
    {
        var mapping = torrent.State.ToMappingResult();

        return new DownloadInfo
        {
            ExternalId = externalId,
            Status = mapping.Status,
            StatusReason = mapping.StatusReason,
            OutputPath = ResolveOutputPath(torrent),
            ProgressPercent = (decimal)torrent.Progress * 100m,
            HeldByPolicy = mapping.IsHeldByPolicy(
                torrent.Ratio, torrent.MaxRatio, torrent.SeedingTime, torrent.MaxSeedingTime)
        };
    }

    public Task<IntegrationSettingsValidationResult> ValidateSettingsAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<IntegrationSettingsValidationIssue>();

        if (settings.TryGetValue("host", out var host) && !string.IsNullOrWhiteSpace(host))
        {
            if (!Uri.TryCreate(host, UriKind.Absolute, out var uri) ||
                !(uri.Scheme is "http" or "https"))
            {
                issues.Add(new IntegrationSettingsValidationIssue(
                    "host",
                    "Host must be an absolute http(s) URL."));
            }
        }

        var hasUsername = settings.TryGetValue("username", out var username) &&
            !string.IsNullOrEmpty(username);
        var hasPassword = settings.TryGetValue("password", out var password) &&
            !string.IsNullOrEmpty(password);

        if (hasUsername && !hasPassword)
        {
            issues.Add(new IntegrationSettingsValidationIssue(
                "password",
                "Username and password must be supplied together (anonymous mode requires both empty)."));
        }
        else if (!hasUsername && hasPassword)
        {
            issues.Add(new IntegrationSettingsValidationIssue(
                "username",
                "Username and password must be supplied together (anonymous mode requires both empty)."));
        }

        return Task.FromResult(new IntegrationSettingsValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues
        });
    }

    public Task<DownloadInfo> DownloadReleaseAsync(
        Release release,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
        => DownloadReleaseAsync(release, new ReleaseGrabContext(), settings, cancellationToken);

    public async Task<DownloadInfo> DownloadReleaseAsync(
        Release release,
        ReleaseGrabContext context,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        var parsed = QbittorrentSettings.Parse(settings);

        if (release.InfoUrl is null)
        {
            throw new InvalidOperationException(
                $"Release {release.Title} has no InfoUrl. qBittorrent expects a magnet URI " +
                "or a .torrent download URL.");
        }

        var tags = string.IsNullOrWhiteSpace(context.ContentType) ? null : context.ContentType;

        var infoUrl = release.InfoUrl.ToString();
        if (infoUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            string? magnetHash = null;
            try
            {
                magnetHash = ExtractV1InfoHash(infoUrl);
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
            {
                _logger.LogWarning(
                    ex,
                    "The qBittorrent integration cannot parse the info hash of the magnet URI for release " +
                    "{Title}. The integration will find the torrent by name.",
                    release.Title);
            }

            try
            {
                await _client.AddMagnetAsync(parsed, infoUrl, tags, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (QbittorrentAddRejectedException ex) when (magnetHash is not null && ex.IsFailsBody)
            {
                var existing = await FindTorrentByHashAsync(_client, parsed, magnetHash, cancellationToken)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    return ProjectDuplicateMagnet(magnetHash, existing);
                }

                throw;
            }

            if (magnetHash is null)
            {
                var added = await _client.FindTorrentByNameAsync(parsed, release.Title, cancellationToken)
                    .ConfigureAwait(false);
                magnetHash = added is not null && !string.IsNullOrEmpty(added.Hash) ? added.Hash : null;
            }

            if (magnetHash is null)
            {
                throw new InvalidOperationException(
                    $"Failed to determine the info-hash of magnet release {release.Title} after " +
                    "adding it to qBittorrent. The magnet used an unsupported hash form and the " +
                    "client did not return a matching entry on lookup.");
            }

            return new DownloadInfo
            {
                ExternalId = magnetHash,
                Status = DownloadStatus.Grabbed
            };
        }

        if (!release.InfoUrl.IsAbsoluteUri ||
            !(release.InfoUrl.Scheme is "http" or "https"))
        {
            throw new InvalidOperationException(
                $"Release {release.Title} InfoUrl scheme {release.InfoUrl.Scheme} is not " +
                "supported. qBittorrent expects 'magnet:', 'http:', or 'https:'.");
        }

        var torrentBytes = await DefaultDownloadTorrentBytesAsync(release.InfoUrl, cancellationToken)
            .ConfigureAwait(false);

        var fileName = InferTorrentFileName(release.InfoUrl);
        string? resolvedHash = null;
        try
        {
            resolvedHash = torrentBytes.ComputeInfoHash();
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            _logger.LogWarning(
                ex,
                "The qBittorrent integration cannot compute the v1 info hash from the .torrent bytes for " +
                "release {Title}. The integration will find the torrent by name.",
                release.Title);
        }

        await _client.AddTorrentFileAsync(parsed, torrentBytes, fileName, tags, cancellationToken)
            .ConfigureAwait(false);

        if (resolvedHash is null)
        {
            var added = await _client.FindTorrentByNameAsync(parsed, release.Title, cancellationToken)
                .ConfigureAwait(false);
            if (added is not null && !string.IsNullOrEmpty(added.Hash))
            {
                resolvedHash = added.Hash;
            }
        }

        if (resolvedHash is null)
        {
            throw new InvalidOperationException(
                $"Failed to determine the info-hash of release {release.Title} after " +
                "adding it to qBittorrent. The .torrent payload was not parseable and the " +
                "client did not return a matching entry on lookup.");
        }

        return new DownloadInfo
        {
            ExternalId = resolvedHash,
            Status = DownloadStatus.Grabbed
        };
    }

    private async Task<byte[]> DefaultDownloadTorrentBytesAsync(
        Uri torrentUrl, CancellationToken cancellationToken)
    {
        using var response = await _torrentDownloadHttpClient.GetAsync(torrentUrl, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
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

        var parsed = QbittorrentSettings.Parse(settings);
        var torrents = await _client.GetTorrentInfoAsync(parsed, externalId, cancellationToken)
            .ConfigureAwait(false);

        if (torrents.Count == 0)
        {
            return new DownloadInfo
            {
                ExternalId = externalId,
                Status = DownloadStatus.Failed,
                StatusReason = "Torrent not found in client"
            };
        }

        var torrent = torrents[0];
        return ProjectTorrent(externalId, torrent);
    }

    public async Task<BatchDownloadStatusResult> GetDownloadsAsync(
        IEnumerable<string> externalIds,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        var requested = new HashSet<string>();
        foreach (var id in externalIds)
        {
            if (!string.IsNullOrEmpty(id))
            {
                requested.Add(id);
            }
        }

        var statuses = new Dictionary<string, DownloadInfo>();
        var failures = new Dictionary<string, Exception>();
        if (requested.Count == 0)
        {
            return new BatchDownloadStatusResult(statuses, failures);
        }

        var parsed = QbittorrentSettings.Parse(settings);
        IReadOnlyList<TorrentInfo> torrents;
        try
        {
            torrents = await _client.GetTorrentInfoAsync(parsed, [.. requested], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            foreach (var id in requested)
            {
                failures[id] = ex;
            }

            return new BatchDownloadStatusResult(statuses, failures);
        }

        foreach (var torrent in torrents)
        {
            if (string.IsNullOrEmpty(torrent.Hash) || !requested.Contains(torrent.Hash))
            {
                continue;
            }

            statuses[torrent.Hash] = ProjectTorrent(torrent.Hash, torrent);
        }

        foreach (var id in requested)
        {
            if (statuses.ContainsKey(id))
            {
                continue;
            }

            statuses[id] = new DownloadInfo
            {
                ExternalId = id,
                Status = DownloadStatus.Failed,
                StatusReason = "Torrent not found in client"
            };
        }

        return new BatchDownloadStatusResult(statuses, failures);
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

        var parsed = QbittorrentSettings.Parse(settings);
        return await _client.DeleteAsync(parsed, downloadId, deleteFiles, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> PauseDownloadAsync(
        string externalId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(externalId))
        {
            throw new ArgumentException("An external ID is required.", nameof(externalId));
        }

        var parsed = QbittorrentSettings.Parse(settings);
        return await _client.PauseAsync(parsed, externalId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ResumeDownloadAsync(
        string externalId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(externalId))
        {
            throw new ArgumentException("An external ID is required.", nameof(externalId));
        }

        var parsed = QbittorrentSettings.Parse(settings);
        return await _client.ResumeAsync(parsed, externalId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ForceStartDownloadAsync(
        string externalId,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(externalId))
        {
            throw new ArgumentException("An external ID is required.", nameof(externalId));
        }

        var parsed = QbittorrentSettings.Parse(settings);
        return await _client.SetForceStartAsync(parsed, externalId, true, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ConfigurationTestResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        QbittorrentSettings parsed;
        try
        {
            parsed = QbittorrentSettings.Parse(settings);
        }
        catch (ArgumentException ex)
        {
            return new ConfigurationTestResult
            {
                Success = false,
                Message = ex.Message,
                FailureKind = ConfigurationFailureKind.Fatal
            };
        }

        if (parsed.HasCredentials)
        {
            var loggedIn = await _client.LoginAsync(parsed, cancellationToken).ConfigureAwait(false);
            if (!loggedIn)
            {
                return new ConfigurationTestResult
                {
                    Success = false,
                    Message = "Authentication required or invalid credentials",
                    FailureKind = ConfigurationFailureKind.Auth
                };
            }
        }

        try
        {
            var version = await _client.GetVersionAsync(parsed, cancellationToken).ConfigureAwait(false);
            return new ConfigurationTestResult
            {
                Success = true,
                Message = $"Connected to qBittorrent {version}",
                FailureKind = ConfigurationFailureKind.None
            };
        }
        catch (QbittorrentAuthenticationException)
        {
            return new ConfigurationTestResult
            {
                Success = false,
                Message = "Authentication required or invalid credentials",
                FailureKind = ConfigurationFailureKind.Auth
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "The qBittorrent connection test failed for {Host}.", parsed.Host);
            return new ConfigurationTestResult
            {
                Success = false,
                Message = ex.Message,
                FailureKind = ConfigurationFailureKind.Fatal
            };
        }
        catch (TaskCanceledException ex)
        {
            return new ConfigurationTestResult
            {
                Success = false,
                Message = $"Timed out contacting qBittorrent: {ex.Message}",
                FailureKind = ConfigurationFailureKind.Transient
            };
        }
    }

    public void Dispose()
    {
        _torrentDownloadHttpClient.Dispose();
    }

    internal static string ExtractV1InfoHash(string magnetUri)
    {
        if (string.IsNullOrWhiteSpace(magnetUri))
        {
            throw new ArgumentException("Magnet URI is empty.", nameof(magnetUri));
        }

        var magnetLink = ParseMagnetLink(magnetUri);
        var v1 = magnetLink.InfoHashes.V1;
        if (v1 is null)
        {
            throw new NotSupportedException("Magnet URI does not contain a v1 (urn:btih:) info hash.");
        }

        return v1.ToHex().ToLowerInvariant();
    }

    private static MagnetLink ParseMagnetLink(string magnetUri)
    {
        if (MagnetLink.TryParse(magnetUri, out var magnetLink))
        {
            return magnetLink!;
        }

        try
        {
            return MagnetLink.Parse(magnetUri);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            throw new ArgumentException(ex.Message, nameof(magnetUri), ex);
        }
    }
}
