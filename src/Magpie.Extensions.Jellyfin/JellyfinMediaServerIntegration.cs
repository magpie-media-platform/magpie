using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Jellyfin.Models;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Caching;
using Magpie.Sdk.Integrations;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Jellyfin;

internal sealed class JellyfinMediaServerIntegration(
    ICacheFactory cacheFactory,
    ILogger<JellyfinMediaServerIntegration> logger,
    IJellyfinClient client) :
        IMediaServerIntegration,
        IRefreshableMediaServerIntegration,
        ILinkableMediaServerIntegration,
        IValidatesIntegrationSettings
{
    private static readonly TimeSpan ResolutionCacheTtl = TimeSpan.FromSeconds(60);
    public const string ProviderTypeName = Constants.ProviderTypeName;
    public const string LibraryOptionsFunctionName = "magpieJellyfin.listLibraries";

    private readonly ILogger<JellyfinMediaServerIntegration> _logger = logger;
    private readonly IJellyfinClient _client = client;

    private readonly ICache _resolutionCache = cacheFactory.GetOrCreate(
        "jellyfin-item-resolution",
        () => new CachePolicy
        {
            Freshness = ResolutionCacheTtl,
            Retention = ResolutionCacheTtl,
            MaximumEntries = int.MaxValue
        });

    private const string ServerWideCacheScope = "*server-wide*";

    public string ProviderType => ProviderTypeName;

    public string DisplayName => Constants.DisplayName;

    public IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; } =
    [
        new()
        {
            Key = JellyfinSettings.HostKey,
            Type = DataType.String,
            DisplayName = "Host URL",
            Description = "Base URL of the Jellyfin server (e.g. http://localhost:8096).",
            Required = true,
            Placeholder = "http://localhost:8096",
        },
        new()
        {
            Key = JellyfinSettings.ApiKeyKey,
            Type = DataType.String,
            DisplayName = "API Key",
            Secret = true,
            Description = "Generated in Jellyfin admin -> Dashboard -> API Keys.",
            Required = true,
        },
        new()
        {
            Key = Constants.LibraryMappingsSettingKey,
            Type = DataType.MappingsList,
            DisplayName = "Library mappings",
            Description =
                "Map Magpie root folders and content types to Jellyfin libraries so refreshes and links " +
                "target the correct library. Leave mappings empty to refresh every library.",
            Required = false,
            RowSchema =
            [
                new()
                {
                    Key = Constants.MappingRootFolderPathKey,
                    Type = DataType.String,
                    DisplayName = "Root folder",
                    Description = "Root folder this mapping applies to.",
                    Required = true,
                    OptionsSource = new SettingSchemaOptionsSource
                    {
                        Kind = SettingOptionsSourceKind.RootFolders,
                    },
                },
                new()
                {
                    Key = Constants.MappingContentTypeKey,
                    Type = DataType.String,
                    DisplayName = "Content type",
                    Description = "Content type this mapping applies to.",
                    Required = true,
                    OptionsSource = new SettingSchemaOptionsSource
                    {
                        Kind = SettingOptionsSourceKind.ContentTypes,
                    },
                },
                new()
                {
                    Key = Constants.MappingServerLibraryIdKey,
                    Type = DataType.String,
                    DisplayName = "Jellyfin library",
                    Description = "Choose a library from the configured Jellyfin server.",
                    Required = true,
                    OptionsSource = new SettingSchemaOptionsSource
                    {
                        Kind = SettingOptionsSourceKind.JSFunction,
                        FunctionName = LibraryOptionsFunctionName,
                    },
                },
            ],
        },
    ];

    private static string DescribeExternalIds(IReadOnlyDictionary<string, string> externalIds)
    {
        var pairs = new List<string>(externalIds.Count);
        foreach (var pair in externalIds)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            pairs.Add(pair.Key + "=" + pair.Value);
        }

        pairs.Sort(StringComparer.Ordinal);
        return pairs.Count == 0 ? "(none)" : string.Join(",", pairs);
    }

    private static string? MatchLibraryId(
        List<Dictionary<string, string>> rows,
        string contentType,
        string rootFolderPath)
    {
        if (string.IsNullOrWhiteSpace(contentType) || string.IsNullOrWhiteSpace(rootFolderPath))
        {
            return null;
        }

        var contentPath = Path.GetFullPath(rootFolderPath);

        var bestLength = -1;
        string? match = null;
        foreach (var row in rows)
        {
            var rowContentType = GetRowValue(row, Constants.MappingContentTypeKey);
            var rowRootFolderPath = GetRowValue(row, Constants.MappingRootFolderPathKey);
            if (string.IsNullOrWhiteSpace(rowContentType) || string.IsNullOrWhiteSpace(rowRootFolderPath))
            {
                continue;
            }

            var mappedRoot = Path.GetFullPath(rowRootFolderPath);
            if (!string.Equals(rowContentType, contentType, StringComparison.Ordinal) ||
                mappedRoot.Length <= bestLength)
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(mappedRoot, contentPath);
            if (Path.IsPathRooted(relativePath) || relativePath == ".." ||
                relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            var libraryId = GetRowValue(row, Constants.MappingServerLibraryIdKey);
            if (string.IsNullOrWhiteSpace(libraryId))
            {
                continue;
            }

            match = libraryId;
            bestLength = mappedRoot.Length;
        }

        return match;
    }

    private static string? GetRowValue(IReadOnlyDictionary<string, string> row, string key)
    {
        return row.TryGetValue(key, out var value) ? value : null;
    }

    private static string CanonicalizeSettings(JellyfinSettings parsed)
    {
        var builder = new StringBuilder();
        builder.Append("host=").Append(parsed.Host.AbsoluteUri).Append('\n');
        builder.Append("apiKey=").Append(parsed.ApiKey).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    public Task<IntegrationSettingsValidationResult> ValidateSettingsAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<IntegrationSettingsValidationIssue>();

        if (settings.TryGetValue(JellyfinSettings.HostKey, out var host) && !string.IsNullOrWhiteSpace(host))
        {
            if (!Uri.TryCreate(host, UriKind.Absolute, out var uri) ||
                !(uri.Scheme is "http" or "https"))
            {
                issues.Add(new IntegrationSettingsValidationIssue(
                    JellyfinSettings.HostKey,
                    "Host must be an absolute http(s) URL."));
            }
        }

        return Task.FromResult(new IntegrationSettingsValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues
        });
    }

    public async Task<ConfigurationTestResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        JellyfinSettings parsed;
        try
        {
            parsed = settings.ToJellyfinSettings();
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

        try
        {
            var info = await _client.GetSystemInfoAsync(parsed, cancellationToken).ConfigureAwait(false);
            var name = string.IsNullOrEmpty(info.ServerName) ? "(unknown)" : info.ServerName;
            var version = string.IsNullOrEmpty(info.Version) ? "(unknown)" : info.Version;
            return new ConfigurationTestResult
            {
                Success = true,
                Message = $"Connected to Jellyfin {name} {version}",
                FailureKind = ConfigurationFailureKind.None
            };
        }
        catch (JellyfinAuthenticationException)
        {
            return new ConfigurationTestResult
            {
                Success = false,
                Message = "Authentication required or invalid API key",
                FailureKind = ConfigurationFailureKind.Auth
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "The Jellyfin connection test failed for {Host}.", parsed.Host);
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
                Message = $"Timed out contacting Jellyfin: {ex.Message}",
                FailureKind = ConfigurationFailureKind.Transient
            };
        }
    }

    public async Task<bool> RefreshForContentAsync(
        MediaServer server,
        IReadOnlyDictionary<string, string> contentExternalIds,
        string contentType,
        string rootFolderPath,
        CancellationToken cancellationToken = default)
    {
        JellyfinSettings parsed;
        try
        {
            parsed = server.Settings.ToJellyfinSettings();
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "The Jellyfin refresh rejected invalid settings.");
            return false;
        }

        var libraryId = ResolveLibraryId(server, contentType, rootFolderPath);
        if (libraryId is null)
        {
            return await _client.RefreshAllLibrariesAsync(parsed, cancellationToken).ConfigureAwait(false);
        }

        return await _client.RefreshLibraryAsync(parsed, libraryId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Uri?> BuildItemDeepLinkAsync(
        MediaServer server,
        IReadOnlyDictionary<string, string> externalIds,
        string contentType,
        string rootFolderPath,
        string title,
        CancellationToken cancellationToken = default)
    {
        JellyfinSettings parsed;
        try
        {
            parsed = server.Settings.ToJellyfinSettings();
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (externalIds.Count == 0)
        {
            return null;
        }

        var mappingRows = server.ToJellyfinMappingRows(_logger);
        var libraryId = mappingRows.Count > 0
            ? MatchLibraryId(mappingRows, contentType, rootFolderPath)
            : null;
        if (string.IsNullOrWhiteSpace(libraryId))
        {
            libraryId = null;
            _logger.LogDebug(
                "The deep link for Jellyfin server {ServerId} will use a server-wide lookup ({Reason}) for " +
                "{ContentType} under {RootFolderPath}.",
                server.Id,
                mappingRows.Count == 0 ? "no library mappings configured" : "no mapping matches",
                contentType,
                rootFolderPath);
        }

        var resolution = await ResolveItemIdAsync(
            server, parsed, externalIds, title, libraryId, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(resolution.ItemId))
        {
            var reason = resolution.Status == DeepLinkLookupStatus.LookupError
                ? "lookup-error"
                : "not-found-on-server";
            _logger.LogInformation(
                "The deep link for Jellyfin server {ServerId} found no item ({Reason}) in the {Scope} lookup " +
                "for external ids {ExternalIds}.",
                server.Id,
                reason,
                libraryId is null ? "server-wide" : "library " + libraryId,
                DescribeExternalIds(externalIds));
            return null;
        }

        var itemId = resolution.ItemId;
        var baseUrl = parsed.Host.AbsoluteUri.TrimEnd('/');
        var detailsUrl = baseUrl + "/web/index.html#!/details?id=" + Uri.EscapeDataString(itemId!);
        return Uri.TryCreate(detailsUrl, UriKind.Absolute, out var detailsUri) ? detailsUri : null;
    }

    private async Task<DeepLinkLookupResult> ResolveItemIdAsync(
        MediaServer server,
        JellyfinSettings parsed,
        IReadOnlyDictionary<string, string> externalIds,
        string title,
        string? libraryId,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildResolutionCacheKey(server.Id, parsed, externalIds, libraryId);
        if (_resolutionCache.TryGet<DeepLinkLookupResult>(cacheKey, out var cached))
        {
            Telemetry.DeepLinkLookups.Add(
                1,
                Telemetry.ProviderType,
                new KeyValuePair<string, object?>("outcome", "cache_hit"));
            return cached;
        }

        var result = await _client
            .TryFindItemIdByExternalIdsAsync(parsed, externalIds, title, libraryId, cancellationToken)
            .ConfigureAwait(false);

        Telemetry.DeepLinkLookups.Add(
            1,
            Telemetry.ProviderType,
            new KeyValuePair<string, object?>(
                "outcome",
                result.Status switch
                {
                    DeepLinkLookupStatus.Resolved => "resolved",
                    DeepLinkLookupStatus.NotFound => "not_found",
                    _ => "lookup_error",
                }));

        if (result.Status != DeepLinkLookupStatus.LookupError)
        {
            _resolutionCache.Set(cacheKey, result);
        }

        return result;
    }

    private string BuildResolutionCacheKey(
        Guid serverId,
        JellyfinSettings parsed,
        IReadOnlyDictionary<string, string> externalIds,
        string? libraryId)
    {
        var builder = new StringBuilder();
        builder.Append(serverId.ToString("N")).Append('|');
        builder.Append(CanonicalizeSettings(parsed)).Append('|');
        builder.Append(string.IsNullOrWhiteSpace(libraryId) ? ServerWideCacheScope : libraryId).Append('|');
        builder.Append(DescribeExternalIds(externalIds));
        return builder.ToString();
    }

    private string? ResolveLibraryId(MediaServer server, string contentType, string rootFolderPath)
    {
        return MatchLibraryId(
            server.ToJellyfinMappingRows(_logger),
            contentType,
            rootFolderPath);
    }
}
