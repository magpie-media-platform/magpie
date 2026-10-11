using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Sdk.Caching;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Qbittorrent;

internal sealed class QbittorrentClient(
    HttpClient http,
    ILogger<QbittorrentClient> logger,
    ICacheFactory cacheFactory) : IQbittorrentClient, IDisposable
{
    private static readonly Version Qbit5BoundaryVersion = new(5, 0);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private const string LoginPath = "api/v2/auth/login";
    private const string VersionPath = "api/v2/app/version";
    private const string AddPath = "api/v2/torrents/add";
    private const string InfoPath = "api/v2/torrents/info";
    private const string DeletePath = "api/v2/torrents/delete";
    private const string PausePath = "api/v2/torrents/pause";
    private const string ResumePath = "api/v2/torrents/resume";
    private const string StopPath = "api/v2/torrents/stop";
    private const string StartPath = "api/v2/torrents/start";
    private const string SetForceStartPath = "api/v2/torrents/setForceStart";

    private readonly ConcurrentDictionary<QbittorrentSettings, byte> _authenticatedSettings = new();

    private readonly SemaphoreSlim _loginGate = new(1, 1);
    private bool _disposed;

    private readonly ICache _versions = cacheFactory.GetOrCreate("qbittorrent-versions", () => new CachePolicy
    {
        Freshness = TimeSpan.FromMinutes(5),
        Retention = TimeSpan.FromMinutes(5),
        MaximumEntries = 64,
    });

    private static bool IsJsonTotalFailure(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = document.RootElement;
            var failureCount = root.TryGetProperty("failure_count", out var failure) &&
                failure.ValueKind == JsonValueKind.Number
                ? failure.GetInt32()
                : 0;
            var successCount = root.TryGetProperty("success_count", out var success) &&
                success.ValueKind == JsonValueKind.Number
                ? success.GetInt32()
                : 0;
            var hasAddedIds = root.TryGetProperty("added_torrent_ids", out var added) &&
                added.ValueKind == JsonValueKind.Array &&
                added.GetArrayLength() > 0;

            return failureCount > 0 && successCount == 0 && !hasAddedIds;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseQbittorrentVersion(string? raw, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(1);
        }

        var cutoff = trimmed.Length;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c is not (>= '0' and <= '9' or '.'))
            {
                cutoff = i;
                break;
            }
        }

        if (cutoff <= 0)
        {
            return false;
        }

        var numeric = trimmed.Substring(0, cutoff);
        return Version.TryParse(numeric, out version);
    }

    private static HttpRequestMessage CreateRequest(
        QbittorrentSettings settings,
        HttpMethod method,
        string path)
    {
        var request = new HttpRequestMessage(method, new Uri(settings.Host, path));
        request.Headers.Referrer = settings.Host;
        return request;
    }

    private static HttpRequestMessage BuildDeleteRequest(
        QbittorrentSettings settings,
        string hash,
        bool deleteFiles)
    {
        var request = CreateRequest(settings, HttpMethod.Post, DeletePath);
        request.Content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("hashes", hash),
            new KeyValuePair<string, string>("deleteFiles", deleteFiles ? "true" : "false")
        ]);
        return request;
    }

    private static HttpRequestMessage BuildHashMutationRequest(
        QbittorrentSettings settings,
        string path,
        string hash)
    {
        var request = CreateRequest(settings, HttpMethod.Post, path);
        request.Content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("hashes", hash)
        ]);
        return request;
    }

    private static HttpRequestMessage BuildForceStartRequest(
        QbittorrentSettings settings,
        string hash,
        bool value)
    {
        var request = CreateRequest(settings, HttpMethod.Post, SetForceStartPath);
        request.Content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("hashes", hash),
            new KeyValuePair<string, string>("value", value ? "true" : "false")
        ]);
        return request;
    }

    private static HttpRequestMessage BuildAddRequest(
        QbittorrentSettings settings,
        string magnetUri,
        string? tags)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(magnetUri), "urls" }
        };
        AddCategoryTagsAndPausedFields(settings, content, tags);

        var request = CreateRequest(settings, HttpMethod.Post, AddPath);
        request.Content = content;
        return request;
    }

    private static HttpRequestMessage BuildAddFileRequest(
        QbittorrentSettings settings,
        byte[] torrentBytes,
        string fileName,
        string? tags)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(torrentBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");

        var safeFileName = string.IsNullOrEmpty(fileName) ? "release.torrent" : fileName;
        content.Add(fileContent, "torrents", safeFileName);
        AddCategoryTagsAndPausedFields(settings, content, tags);

        var request = CreateRequest(settings, HttpMethod.Post, AddPath);
        request.Content = content;
        return request;
    }

    private static void AddCategoryTagsAndPausedFields(
        QbittorrentSettings settings,
        MultipartFormDataContent content,
        string? tags)
    {
        if (!string.IsNullOrWhiteSpace(settings.Category))
        {
            content.Add(new StringContent(settings.Category), "category");
        }

        if (!string.IsNullOrWhiteSpace(tags))
        {
            content.Add(new StringContent(tags), "tags");
        }

        var paused = settings.AddPaused ? "true" : "false";
        content.Add(new StringContent(paused), "paused");
        content.Add(new StringContent(paused), "stopped");
    }

    public async Task<string> GetVersionAsync(
        QbittorrentSettings settings,
        CancellationToken cancellationToken)
    {
        await EnsureLoggedInAsync(settings, cancellationToken).ConfigureAwait(false);
        using var response = await SendWithRetryAsync(
            settings,
            () => CreateRequest(settings, HttpMethod.Get, VersionPath),
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
    }

    public async Task<IReadOnlyList<TorrentInfo>> GetTorrentInfoAsync(
        QbittorrentSettings settings,
        IReadOnlyCollection<string> hashes,
        CancellationToken cancellationToken)
    {
        if (hashes.Count == 0)
        {
            return [];
        }

        await EnsureLoggedInAsync(settings, cancellationToken).ConfigureAwait(false);
        var joined = string.Join("|", hashes);
        var path = $"{InfoPath}?hashes={Uri.EscapeDataString(joined)}";
        using var response = await SendWithRetryAsync(
            settings,
            () => CreateRequest(settings, HttpMethod.Get, path),
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        var parsed = JsonSerializer.Deserialize<TorrentInfo[]>(body, JsonOptions);
        return parsed ?? [];
    }

    public async Task<bool> DeleteAsync(
        QbittorrentSettings settings,
        string hash,
        bool deleteFiles,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(hash))
        {
            throw new ArgumentException("Hash is required.", nameof(hash));
        }

        await EnsureLoggedInAsync(settings, cancellationToken).ConfigureAwait(false);

        using var response = await SendWithRetryAsync(
            settings,
            () => BuildDeleteRequest(settings, hash, deleteFiles),
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> PauseAsync(
        QbittorrentSettings settings,
        string hash,
        CancellationToken cancellationToken)
    {
        var path = await IsAtLeastVersion5Async(settings, cancellationToken).ConfigureAwait(false)
            ? StopPath
            : PausePath;
        return await SendHashMutationAsync(settings, path, hash, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ResumeAsync(
        QbittorrentSettings settings,
        string hash,
        CancellationToken cancellationToken)
    {
        var path = await IsAtLeastVersion5Async(settings, cancellationToken).ConfigureAwait(false)
            ? StartPath
            : ResumePath;
        return await SendHashMutationAsync(settings, path, hash, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SetForceStartAsync(
        QbittorrentSettings settings,
        string hash,
        bool value,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(hash))
        {
            throw new ArgumentException("Hash is required.", nameof(hash));
        }

        await EnsureLoggedInAsync(settings, cancellationToken).ConfigureAwait(false);

        using var response = await SendWithRetryAsync(
            settings,
            () => BuildForceStartRequest(settings, hash, value),
            cancellationToken).ConfigureAwait(false);
        return InterpretMutation(response, $"{SetForceStartPath} (hash {hash})");
    }

    public async Task AddTorrentFileAsync(
        QbittorrentSettings settings,
        byte[] torrentBytes,
        string fileName,
        string? tags,
        CancellationToken cancellationToken)
    {
        await EnsureLoggedInAsync(settings, cancellationToken).ConfigureAwait(false);

        using var response = await SendWithRetryAsync(
            settings,
            () => BuildAddFileRequest(settings, torrentBytes, fileName, tags),
            cancellationToken).ConfigureAwait(false);
        await EnsureAddAcceptedAsync(response, $".torrent file {fileName}").ConfigureAwait(false);
    }

    public async Task<TorrentInfo?> FindTorrentByNameAsync(
        QbittorrentSettings settings,
        string name,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        await EnsureLoggedInAsync(settings, cancellationToken).ConfigureAwait(false);

        using var response = await SendWithRetryAsync(
            settings,
            () => CreateRequest(settings, HttpMethod.Get, InfoPath),
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var parsed = JsonSerializer.Deserialize<TorrentInfo[]>(body, JsonOptions) ?? [];
        foreach (var torrent in parsed)
        {
            if (string.Equals(torrent.Name, name, StringComparison.Ordinal))
            {
                return torrent;
            }
        }

        return null;
    }

    public async Task AddMagnetAsync(
        QbittorrentSettings settings,
        string magnetUri,
        string? tags,
        CancellationToken cancellationToken)
    {
        await EnsureLoggedInAsync(settings, cancellationToken).ConfigureAwait(false);

        using var response = await SendWithRetryAsync(
            settings,
            () => BuildAddRequest(settings, magnetUri, tags),
            cancellationToken).ConfigureAwait(false);
        await EnsureAddAcceptedAsync(response, "magnet URI").ConfigureAwait(false);
    }

    private async Task EnsureAddAcceptedAsync(HttpResponseMessage response, string what)
    {
        if (response.StatusCode == HttpStatusCode.UnsupportedMediaType)
        {
            throw new ReleasePayloadRejectedException(
                $"qBittorrent rejected the {what}: the .torrent file is not valid (HTTP 415).");
        }

        response.EnsureSuccessStatusCode();

        var body = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();

        if (string.Equals(body, "Ok.", StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(body, "Fails.", StringComparison.Ordinal))
        {
            Reject(body);
        }

        if (body.Length > 0 && (body[0] == '{' || body[0] == '['))
        {
            if (IsJsonTotalFailure(body))
            {
                Reject(body);
            }

            return;
        }

        return;

        void Reject(string responseBody)
        {
            logger.LogWarning("qBittorrent rejected the request to add {What} with body {Body}.", what, responseBody);
            throw new QbittorrentAddRejectedException(
                $"qBittorrent rejected the {what} (response {responseBody}).",
                responseBody);
        }
    }

    private async Task<bool> IsAtLeastVersion5Async(
        QbittorrentSettings settings,
        CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        var key = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(
            settings.Host.AbsoluteUri + "\n" + settings.Username + "\n" + settings.Password)));
        if (_versions.TryGet<bool>(key, out var cached))
        {
            return cached;
        }

        try
        {
            var raw = await GetVersionAsync(settings, cancellationToken).ConfigureAwait(false);
            if (TryParseQbittorrentVersion(raw, out var parsed))
            {
                var version5 = parsed >= Qbit5BoundaryVersion;
                _versions.Set(key, version5);
                return version5;
            }

            logger.LogWarning("qBittorrent /app/version returned an invalid version string {Version}.", raw);
        }
        catch (Exception ex) when (
            !cancellationToken.IsCancellationRequested &&
            ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "The qBittorrent version probe failed.");
        }

        return false;
    }

    private async Task<bool> SendHashMutationAsync(
        QbittorrentSettings settings,
        string path,
        string hash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(hash))
        {
            throw new ArgumentException("Hash is required.", nameof(hash));
        }

        await EnsureLoggedInAsync(settings, cancellationToken).ConfigureAwait(false);

        using var response = await SendWithRetryAsync(
            settings,
            () => BuildHashMutationRequest(settings, path, hash),
            cancellationToken).ConfigureAwait(false);
        return InterpretMutation(response, $"{path} (hash {hash})");
    }

    public async Task<bool> LoginAsync(
        QbittorrentSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.HasCredentials || _authenticatedSettings.ContainsKey(settings))
        {
            return true;
        }

        await _loginGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_authenticatedSettings.ContainsKey(settings))
            {
                return true;
            }

            using var request = CreateRequest(settings, HttpMethod.Post, LoginPath);
            request.Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("username", settings.Username!),
                new KeyValuePair<string, string>("password", settings.Password!)
            ]);

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                logger.LogWarning(
                    "The qBittorrent login at {Host} failed with HTTP {Status}.",
                    settings.Host,
                    (int)response.StatusCode);
                return false;
            }

            response.EnsureSuccessStatusCode();

            var body = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();

            var ok = body.Length == 0 || string.Equals(body, "Ok.", StringComparison.Ordinal);
            if (ok)
            {
                _authenticatedSettings.TryAdd(settings, 0);
            }
            else
            {
                logger.LogWarning(
                    "The qBittorrent login at {Host} returned {Body} (expected 'Ok.' or an empty body).",
                    settings.Host,
                    body);
            }

            return ok;
        }
        finally
        {
            _loginGate.Release();
        }
    }

    private async Task EnsureLoggedInAsync(
        QbittorrentSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.HasCredentials || _authenticatedSettings.ContainsKey(settings))
        {
            return;
        }

        if (!await LoginAsync(settings, cancellationToken).ConfigureAwait(false))
        {
            throw new QbittorrentAuthenticationException("qBittorrent rejected the credentials.");
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        QbittorrentSettings settings,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        using var firstRequest = requestFactory();
        var response = await http.SendAsync(firstRequest, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
        {
            return response;
        }

        var statusCode = response.StatusCode;
        response.Dispose();
        _authenticatedSettings.TryRemove(settings, out _);
        if (!settings.HasCredentials)
        {
            throw new QbittorrentAuthenticationException(
                $"qBittorrent requires authentication (HTTP {(int)statusCode}).");
        }

        await EnsureLoggedInAsync(settings, cancellationToken).ConfigureAwait(false);
        using var retryRequest = requestFactory();
        response = await http.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            _authenticatedSettings.TryRemove(settings, out _);
            throw new QbittorrentAuthenticationException("qBittorrent rejected the authenticated request.");
        }

        return response;
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
            logger.LogInformation("The qBittorrent request {Description} returned HTTP {Status}.", description, code);
            return false;
        }

        response.EnsureSuccessStatusCode();
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _authenticatedSettings.Clear();
        _loginGate.Dispose();
        http.Dispose();
    }
}
