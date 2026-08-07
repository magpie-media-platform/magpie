using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Jellyfin.Models;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Jellyfin;

internal sealed class JellyfinClient(
    HttpClient http,
    ILogger<JellyfinClient> logger) : IJellyfinClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string SystemInfoPath = "System/Info/Public";
    private const string LibraryRefreshPath = "Library/Refresh";
    private const string VirtualFoldersPath = "Library/VirtualFolders";
    private const string ItemsPath = "Items";

    public const string ApiKeyHeader = "X-Emby-Token";

    public const string HttpClientName = "magpie.jellyfin";

    private readonly HttpClient _http = http;
    private readonly ILogger<JellyfinClient> _logger = logger;

    private static HttpRequestMessage CreateRequest(JellyfinSettings settings, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(settings.Host, path));
        request.Headers.Add(ApiKeyHeader, settings.ApiKey);
        return request;
    }

    private static bool ProviderIdValuesMatch(string expected, string actual)
    {
        if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var expectedTail = TrailingSegment(expected);
        return expectedTail.Length > 0 &&
            string.Equals(expectedTail, TrailingSegment(actual), StringComparison.OrdinalIgnoreCase);
    }

    private static string TrailingSegment(string value)
    {
        var trimmed = value.Trim();
        var separator = trimmed.LastIndexOfAny(['/', '\\', ':', '|']);
        return separator < 0 ? trimmed : trimmed.Substring(separator + 1);
    }

    public async Task<SystemInfo> GetSystemInfoAsync(
        JellyfinSettings settings,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(settings, HttpMethod.Get, SystemInfoPath);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new JellyfinAuthenticationException(
                $"Jellyfin returned HTTP {(int)response.StatusCode} for /System/Info/Public.");
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return new SystemInfo();
        }

        var parsed = JsonSerializer.Deserialize<SystemInfo>(body, JsonOptions);
        return parsed ?? new SystemInfo();
    }

    public async Task<bool> RefreshAllLibrariesAsync(
        JellyfinSettings settings,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(settings, HttpMethod.Post, LibraryRefreshPath);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return InterpretMutation(response, "/Library/Refresh");
    }

    public async Task<bool> RefreshLibraryAsync(
        JellyfinSettings settings,
        string folderId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folderId))
        {
            throw new ArgumentException("A folder ID is required.", nameof(folderId));
        }

        var path = $"Items/{Uri.EscapeDataString(folderId)}/Refresh" +
            "?Recursive=true&ImageRefreshMode=Default&MetadataRefreshMode=Default";
        using var request = CreateRequest(settings, HttpMethod.Post, path);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return InterpretMutation(response, $"/Items/{folderId}/Refresh");
    }

    public async Task<IReadOnlyList<VirtualFolder>> GetVirtualFoldersAsync(
        JellyfinSettings settings,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(settings, HttpMethod.Get, VirtualFoldersPath);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new JellyfinAuthenticationException(
                $"Jellyfin returned HTTP {(int)response.StatusCode} for /Library/VirtualFolders.");
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        var parsed = JsonSerializer.Deserialize<List<VirtualFolder>>(body, JsonOptions);
        return parsed ?? [];
    }

    public async Task<DeepLinkLookupResult> TryFindItemIdByExternalIdsAsync(
        JellyfinSettings settings,
        IReadOnlyDictionary<string, string> externalIds,
        string? title,
        string? parentLibraryId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new DeepLinkLookupResult(null, DeepLinkLookupStatus.NotFound);
        }

        var expected = new List<KeyValuePair<string, string>>(externalIds.Count);
        foreach (var pair in externalIds)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            expected.Add(pair);
        }

        if (expected.Count == 0)
        {
            return new DeepLinkLookupResult(null, DeepLinkLookupStatus.NotFound);
        }

        var query = "?recursive=true&fields=ProviderIds&searchTerm=" +
            Uri.EscapeDataString(title);

        if (!string.IsNullOrWhiteSpace(parentLibraryId))
        {
            query += "&parentId=" + Uri.EscapeDataString(parentLibraryId!);
        }

        var path = ItemsPath + query;

        HttpResponseMessage response;
        try
        {
            using var request = CreateRequest(settings, HttpMethod.Get, path);
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(
                ex,
                "The Jellyfin /Items search for {Title} failed. No deep link is available.",
                title);
            return new DeepLinkLookupResult(null, DeepLinkLookupStatus.LookupError);
        }

        try
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "The Jellyfin /Items search for {Title} returned HTTP {Status}. No deep link is available.",
                    title,
                    (int)response.StatusCode);
                return new DeepLinkLookupResult(null, DeepLinkLookupStatus.LookupError);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return new DeepLinkLookupResult(null, DeepLinkLookupStatus.NotFound);
            }

            ItemsQueryResult? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ItemsQueryResult>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(
                    ex,
                    "The Jellyfin /Items search for {Title} returned invalid JSON. No deep link is available.",
                    title);
                return new DeepLinkLookupResult(null, DeepLinkLookupStatus.LookupError);
            }

            var items = parsed?.Items;
            if (items is null || items.Count == 0)
            {
                return new DeepLinkLookupResult(null, DeepLinkLookupStatus.NotFound);
            }

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || item.ProviderIds is null)
                {
                    continue;
                }

                foreach (var providerEntry in item.ProviderIds)
                {
                    if (string.IsNullOrWhiteSpace(providerEntry.Key) ||
                        string.IsNullOrWhiteSpace(providerEntry.Value))
                    {
                        continue;
                    }

                    foreach (var candidate in expected)
                    {
                        if (string.Equals(candidate.Key, providerEntry.Key, StringComparison.OrdinalIgnoreCase) &&
                            ProviderIdValuesMatch(candidate.Value, providerEntry.Value))
                        {
                            return new DeepLinkLookupResult(item.Id, DeepLinkLookupStatus.Resolved);
                        }
                    }
                }
            }

            _logger.LogDebug(
                "The Jellyfin /Items search for {Title} returned {Count} items without provider ids. " +
                "No deep link is available.",
                title,
                items.Count);
            return new DeepLinkLookupResult(null, DeepLinkLookupStatus.NotFound);
        }
        finally
        {
            response.Dispose();
        }
    }

    private bool InterpretMutation(HttpResponseMessage response, string description)
    {
        var code = (int)response.StatusCode;
        if (code is >= 200 and < 300)
        {
            return true;
        }

        if (code is >= 400 and < 500)
        {
            _logger.LogInformation(
                "The Jellyfin request {Description} returned HTTP {Status}.",
                description,
                code);
            return false;
        }

        response.EnsureSuccessStatusCode();
        return false;
    }
}
