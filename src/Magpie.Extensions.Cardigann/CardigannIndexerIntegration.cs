using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Cardigann.Engine;
using Magpie.Extensions.Cardigann.Engine.Categories;
using Magpie.Extensions.Cardigann.Engine.Session;
using Magpie.Extensions.Cardigann.Engine.Templating;
using Magpie.Extensions.Cardigann.Mapping;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Sdk.Integrations;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Cardigann;

internal sealed class CardigannIndexerIntegration(
    CardigannDefinition definition,
    IResultParser parser,
    ICardigannSessionManager sessions,
    ILogger<CardigannIndexerIntegration> logger) : IIndexerIntegration
{
    public const string BaseUrlSettingKey = "baseUrl";
    public const string UserAgentSettingKey = "userAgent";

    private const string DefaultUserAgentTemplate = "Magpie-Cardigann/1.0 (+{0})";
    private const int DetailFetchConcurrencyLimit = 4;

    public string ProviderType => definition.Id;

    public IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; } =
        definition.Settings.ToSettingSchemaDefinitions(definition.Links);

    public IReadOnlyList<string> SupportedContentTypes { get; } = [];

    public IReadOnlyList<IReadOnlyCollection<string>> SearchFacets { get; } = BuildSearchFacets(definition.Caps);

    public bool SupportsKeywordSearch
    {
        get
        {
            return SearchFacets.Any(facets => facets.Contains("q", StringComparer.OrdinalIgnoreCase));
        }
    }

    private static ConfigurationFailureKind ResolveFailureKind(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ConfigurationFailureKind.Auth,
            HttpStatusCode.NotFound => ConfigurationFailureKind.NotFound,
            HttpStatusCode.RequestTimeout
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout => ConfigurationFailureKind.Transient,
            (HttpStatusCode)429 => ConfigurationFailureKind.RateLimited,
            _ => ConfigurationFailureKind.Fatal,
        };
    }

    private static int? ReadPositiveIntCriteria(ReleaseSearchRequest request, string key)
    {
        var value = ReadFirstCriteria(request, key);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;
    }

    private static string? ReadFirstCriteria(ReleaseSearchRequest request, string key)
    {
        if (!request.Criteria.TryGetValue(key, out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static void AddIdSearchVariable(
        IDictionary<string, object?> vars,
        ReleaseSearchRequest request,
        ISet<string> supported)
    {
        if (request.IdSearchParameter is not { } rawParameter ||
            rawParameter.Trim().ToLowerInvariant() is not { Length: > 0 } parameter ||
            !supported.Contains(parameter) ||
            string.IsNullOrWhiteSpace(request.IdSearchValue))
        {
            return;
        }

        var value = request.IdSearchValue!.Trim();
        switch (parameter)
        {
            case "imdbid":
                vars[".Query.IMDBID"] = value;
                vars[".Query.IMDBIDShort"] = value.StartsWith("tt", StringComparison.OrdinalIgnoreCase)
                    ? value[2..]
                    : value;
                break;
            case "tvdbid":
                vars[".Query.TVDBID"] = value;
                break;
            case "tmdbid":
                vars[".Query.TMDBID"] = value;
                break;
            case "tvmazeid":
                vars[".Query.TVMazeID"] = value;
                break;
            case "traktid":
                vars[".Query.TraktID"] = value;
                break;
            case "sceneid":
                vars[".Query.SceneID"] = value;
                break;
            case "movieid":
                vars[".Query.MovieID"] = value;
                break;
            case "javid":
                vars[".Query.JAVID"] = value;
                break;
        }
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        if (uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
        {
            return uri;
        }

        return new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
    }

    public bool ReleaseMatchesCategories(
        IReadOnlyList<string> releaseCategories,
        IReadOnlyList<string> contentCategories,
        out bool recognised)
    {
        return releaseCategories.MatchesContentCategories(contentCategories, out recognised);
    }

    public async Task<IEnumerable<Release>> SearchAsync(
        ReleaseSearchRequest request,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ResolveNativeCategories(request.Categories) is null)
        {
            return [];
        }

        var baseUri = ResolveBaseUri(settings);

        if (definition.Login is not null)
        {
            if (definition.Login.Captcha is not null)
            {
                logger.LogDebug(
                    "Cardigann does not support the captcha login of tracker {Tracker}. " +
                    "The search will return no results",
                    definition.Id);
                return [];
            }

            var loginOutcome = await EnsureLoginAsync(baseUri, settings, forceRefresh: false, cancellationToken)
                .ConfigureAwait(false);
            if (loginOutcome is { Success: false })
            {
                logger.LogWarning(
                    "Cardigann login failed for tracker {Tracker}. The search will return no results. {Reason}",
                    definition.Id,
                    loginOutcome.Message);
                return [];
            }
        }

        var variables = BuildVariables(request, settings);

        var path = definition.Search.Paths[0];
        var requestUri = BuildRequestUri(baseUri, path, variables);

        var body = await FetchBodyAsync(HttpMethod.Get, requestUri, settings, baseUri, cancellationToken)
            .ConfigureAwait(false);

        var isJson = string.Equals(path.Response?.Type, "json", StringComparison.OrdinalIgnoreCase);
        var releases = isJson
            ? parser.ParseJson(body, definition, baseUri, variables)
            : parser.Parse(body, definition, baseUri, variables);
        var resolved = isJson || definition.Download is null
            ? releases
            : await ResolveDownloadsAsync(releases, definition.Download, baseUri, settings, cancellationToken)
                .ConfigureAwait(false);

        return resolved;
    }

    private static IReadOnlyList<IReadOnlyCollection<string>> BuildSearchFacets(CapabilitiesBlock caps)
    {
        var facets = new List<IReadOnlyCollection<string>>();
        foreach (var mode in caps.Modes.Values)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var param in mode)
            {
                if (!string.IsNullOrWhiteSpace(param))
                {
                    var name = param.Trim();
                    set.Add(string.Equals(name, "ep", StringComparison.OrdinalIgnoreCase) ? "episode" : name);
                }
            }

            facets.Add(set);
        }

        return facets;
    }

    private IReadOnlyList<string>? ResolveNativeCategories(IReadOnlyList<string> contentCategories)
    {
        var mappings = definition.ResolveCategoryMappings();
        if (mappings.Count == 0)
        {
            return [];
        }

        var indexerCategories = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            if (!string.IsNullOrWhiteSpace(mapping.Cat) && seen.Add(mapping.Cat))
            {
                indexerCategories.Add(mapping.Cat);
            }
        }

        if (indexerCategories.Count == 0)
        {
            return [];
        }

        var narrowed = indexerCategories.NarrowToContentCategories(contentCategories);
        if (narrowed is null)
        {
            return null;
        }

        var nativeIds = new List<string>();
        var seenNative = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slug in narrowed)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                continue;
            }

            foreach (var mapping in mappings)
            {
                if ((string.Equals(mapping.Id, slug, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(mapping.Cat, slug, StringComparison.OrdinalIgnoreCase)) &&
                    seenNative.Add(mapping.Id))
                {
                    nativeIds.Add(mapping.Id);
                }
            }
        }

        return nativeIds;
    }

    private async Task<IReadOnlyList<Release>> ResolveDownloadsAsync(
        IReadOnlyList<Release> releases,
        DownloadBlock download,
        Uri baseUri,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        if (releases.Count == 0)
        {
            return releases;
        }

        var resolved = new Release?[releases.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, releases.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = DetailFetchConcurrencyLimit,
                CancellationToken = cancellationToken,
            },
            async (index, token) =>
                resolved[index] = await ResolveOneAsync(releases[index], download, baseUri, settings, token)
                    .ConfigureAwait(false))
            .ConfigureAwait(false);

        return [.. resolved.Where(r => r is not null).Cast<Release>()];
    }

    private async Task<Release?> ResolveOneAsync(
        Release release,
        DownloadBlock download,
        Uri baseUri,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        if (release.InfoUrl is not null &&
            string.Equals(release.InfoUrl.Scheme, "magnet", StringComparison.OrdinalIgnoreCase))
        {
            return release;
        }

        if (release.InfoUrl is null)
        {
            logger.LogWarning(
                "Cardigann detail fetch skipped {Tracker} row {Title}. The row has no detail URL",
                definition.Id,
                release.Title);
            return null;
        }

        var targetUri = release.InfoUrl;
        var method = string.IsNullOrEmpty(download.Method)
            ? HttpMethod.Get
            : new HttpMethod(download.Method!.ToUpperInvariant());

        string body;
        try
        {
            body = await FetchBodyAsync(method, targetUri, settings, baseUri, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "Cardigann detail fetch skipped {Tracker} row {Title} at {Uri}. The request returned an HTTP error",
                definition.Id,
                release.Title,
                targetUri);
            return null;
        }

        var extracted = parser.ExtractDownloadTarget(body, download);
        if (string.IsNullOrEmpty(extracted))
        {
            logger.LogWarning(
                "Cardigann detail fetch skipped a {Tracker} row: selector {Selector} missed row {Title} at {Uri}",
                definition.Id,
                download.Selector,
                release.Title,
                targetUri);
            return null;
        }

        if (!Uri.TryCreate(baseUri, extracted, out var target))
        {
            logger.LogWarning(
                "Cardigann detail fetch skipped a {Tracker} row: URI {Extracted} for row {Title} is not valid",
                definition.Id,
                extracted,
                release.Title);
            return null;
        }

        release.InfoUrl = target;
        parser.ApplyResolvedDownload(release, target);
        return release;
    }

    public async Task<ConfigurationTestResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        var baseUri = ResolveBaseUri(settings);

        if (definition.Login is not null)
        {
            return await TestLoginAsync(baseUri, settings, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var response = await sessions.SendAsync(
                SessionKey(baseUri),
                baseUri,
                HttpMethod.Get,
                baseUri,
                ResolveUserAgent(settings),
                captureCookies: false,
                cancellationToken)
                .ConfigureAwait(false);

            if ((int)response.StatusCode == 200)
            {
                return new ConfigurationTestResult
                {
                    Success = true,
                    Message = $"Connected to {baseUri} ({(int)response.StatusCode})."
                };
            }

            var cloudflareHint = response.DetectCloudflareChallenge();
            var message = cloudflareHint is null
                ? $"Unexpected status {(int)response.StatusCode} {response.ReasonPhrase} from {baseUri}."
                : $"Cloudflare protection detected at {baseUri}: {cloudflareHint}";
            return new ConfigurationTestResult
            {
                Success = false,
                Message = message,
                FailureKind = cloudflareHint is null
                    ? ResolveFailureKind(response.StatusCode)
                    : ConfigurationFailureKind.Fatal
            };
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "Cardigann connection test failed for {Tracker} at {BaseUri}",
                definition.Id,
                baseUri);
            return new ConfigurationTestResult
            {
                Success = false,
                Message = $"Network error: {ex.Message}",
                FailureKind = ConfigurationFailureKind.Fatal
            };
        }
        catch (TaskCanceledException ex)
        {
            return new ConfigurationTestResult
            {
                Success = false,
                Message = $"Timed out contacting {baseUri}: {ex.Message}",
                FailureKind = ConfigurationFailureKind.Transient
            };
        }
    }

    private async Task<string> FetchBodyAsync(
        HttpMethod method,
        Uri requestUri,
        IReadOnlyDictionary<string, string> settings,
        Uri baseUri,
        CancellationToken cancellationToken)
    {
        var login = definition.Login is { Captcha: null } ? definition.Login : null;
        for (var attempt = 0; ; attempt++)
        {
            if (login is not null)
            {
                await EnsureLoginAsync(baseUri, settings, forceRefresh: false, cancellationToken)
                    .ConfigureAwait(false);
            }

            using var response = await sessions.SendAsync(
                SessionKey(baseUri),
                baseUri,
                method,
                requestUri,
                ResolveUserAgent(settings),
                captureCookies: login is not null,
                cancellationToken)
                .ConfigureAwait(false);

            var cloudflareHint = response.DetectCloudflareChallenge();
            if (cloudflareHint is not null)
            {
                throw new HttpRequestException(
                    $"Cardigann tracker {definition.Id} is behind Cloudflare ({cloudflareHint}) and the " +
                    "challenge could not be solved.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (login is not null && attempt == 0 && sessions.IsLoggedOut(login, response.StatusCode, body))
            {
                logger.LogDebug(
                    "Cardigann {Tracker} response shows a logged-out session. Cardigann will log in again",
                    definition.Id);
                sessions.InvalidateSession(SessionKey(baseUri));
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Cardigann tracker {definition.Id} returned HTTP {(int)response.StatusCode} for {requestUri}.");
            }

            return body;
        }
    }

    private string ResolveUserAgent(IReadOnlyDictionary<string, string> settings)
    {
        return settings.TryGetValue(UserAgentSettingKey, out var ua) &&
            !string.IsNullOrWhiteSpace(ua)
                ? ua
                : string.Format(CultureInfo.InvariantCulture, DefaultUserAgentTemplate, definition.Id);
    }

    private string SessionKey(Uri baseUri) => definition.Id + "@" + baseUri.GetLeftPart(UriPartial.Authority);

    private async Task<CardigannLoginOutcome?> EnsureLoginAsync(
        Uri baseUri,
        IReadOnlyDictionary<string, string> settings,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var login = definition.Login;
        if (login is null)
        {
            return null;
        }

        var variables = BuildLoginVariables(settings);
        var cookieValue = ResolveCookieValue(settings);
        var userAgent = ResolveUserAgent(settings);

        return await sessions
            .EnsureLoggedInAsync(
                SessionKey(baseUri),
                login,
                baseUri,
                variables,
                cookieValue,
                userAgent,
                forceRefresh,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ConfigurationTestResult> TestLoginAsync(
        Uri baseUri,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        if (definition.Login!.Captcha is not null)
        {
            logger.LogWarning(
                "The Cardigann login test failed for tracker {Tracker}. Cardigann does not support a captcha login.",
                definition.Id);
            return new ConfigurationTestResult
            {
                Success = false,
                Message = $"{definition.Name} requires a captcha to log in, which is not supported in this build.",
                FailureKind = ConfigurationFailureKind.Auth
            };
        }

        var outcome = await EnsureLoginAsync(baseUri, settings, forceRefresh: true, cancellationToken)
            .ConfigureAwait(false);
        if (outcome is null || outcome.Success)
        {
            return new ConfigurationTestResult
            {
                Success = true,
                Message = $"Logged in to {definition.Name} at {baseUri}."
            };
        }

        return new ConfigurationTestResult
        {
            Success = false,
            Message = outcome.Message ?? "Login failed.",
            FailureKind = outcome.FailureKind
        };
    }

    private string? ResolveCookieValue(IReadOnlyDictionary<string, string> settings)
    {
        foreach (var setting in definition.Settings)
        {
            if (string.Equals(setting.Type, "cookie", StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(setting.Name) &&
                settings.TryGetValue(setting.Name, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private Dictionary<string, object?> BuildLoginVariables(IReadOnlyDictionary<string, string> settings)
    {
        var vars = new Dictionary<string, object?>();
        AddConfigVariables(vars, settings);
        return vars;
    }

    private Dictionary<string, object?> BuildVariables(
        ReleaseSearchRequest request,
        IReadOnlyDictionary<string, string> settings)
    {
        var query = request.Title ?? string.Empty;
        var vars = new Dictionary<string, object?>()
        {
            [".Keywords"] = query,
            [".Query.Keywords"] = query,
            [".Query.Q"] = query,
            [".Categories"] = string.Empty,
        };
        AddConfigVariables(vars, settings);
        AddScopedQueryVariables(vars, request);
        return vars;
    }

    private void AddConfigVariables(IDictionary<string, object?> vars, IReadOnlyDictionary<string, string> settings)
    {
        foreach (var setting in definition.Settings)
        {
            if (string.IsNullOrEmpty(setting.Name))
            {
                continue;
            }

            var value = settings.TryGetValue(setting.Name, out var configured) && !string.IsNullOrEmpty(configured)
                ? configured
                : setting.Default?.ToString() ?? string.Empty;
            vars[$".Config.{setting.Name}"] = value;
        }
    }

    private void AddScopedQueryVariables(IDictionary<string, object?> vars, ReleaseSearchRequest request)
    {
        var supported = GetSupportedSearchParams();
        var season = ReadPositiveIntCriteria(request, "season");
        var episode = ReadPositiveIntCriteria(request, "episode");

        if (season is > 0 && supported.Contains("season"))
        {
            vars[".Query.Season"] = season.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (episode is > 0 && supported.Contains("ep"))
        {
            vars[".Query.Ep"] = episode.Value.ToString(CultureInfo.InvariantCulture);
            if (season is > 0)
            {
                vars[".Query.Episode"] = string.Format(
                    CultureInfo.InvariantCulture,
                    "S{0:00}E{1:00}",
                    season.Value,
                    episode.Value);
            }
        }

        if (request.Year is > 0 && supported.Contains("year"))
        {
            vars[".Query.Year"] = request.Year.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (ReadFirstCriteria(request, "album") is { } album && supported.Contains("album"))
        {
            vars[".Query.Album"] = album;
        }

        if (ReadFirstCriteria(request, "artist") is { } artist && supported.Contains("artist"))
        {
            vars[".Query.Artist"] = artist;
        }

        if (ReadFirstCriteria(request, "track") is { } track && supported.Contains("track"))
        {
            vars[".Query.Track"] = track;
        }

        if (ReadFirstCriteria(request, "author") is { } author && supported.Contains("author"))
        {
            vars[".Query.Author"] = author;
        }

        AddIdSearchVariable(vars, request, supported);
        AddCategoryVariables(vars, request);
    }

    private void AddCategoryVariables(IDictionary<string, object?> vars, ReleaseSearchRequest request)
    {
        var nativeIds = ResolveNativeCategories(request.Categories);
        if (nativeIds is { Count: > 0 })
        {
            vars[".Categories"] = nativeIds;
        }
    }

    private ISet<string> GetSupportedSearchParams()
    {
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mode in definition.Caps.Modes)
        {
            foreach (var param in mode.Value)
            {
                if (!string.IsNullOrWhiteSpace(param))
                {
                    supported.Add(param.Trim());
                }
            }
        }

        return supported;
    }

    private Uri BuildRequestUri(Uri baseUri, SearchPath path, IReadOnlyDictionary<string, object?> variables)
    {
        var renderedPath = path.Path.RenderCardigannTemplate(variables);

        var inputs = new Dictionary<string, string>();
        foreach (var pair in definition.Search.Inputs)
        {
            inputs[pair.Key] = pair.Value;
        }

        if (path.Inputs is not null)
        {
            foreach (var pair in path.Inputs)
            {
                inputs[pair.Key] = pair.Value;
            }
        }

        var query = new List<string>();
        foreach (var (key, template) in inputs)
        {
            var value = template.RenderCardigannTemplate(variables);
            if (string.Equals(key, "$raw", StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(value))
                {
                    query.Add(value);
                }

                continue;
            }

            query.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }

        var pathWithQuery = renderedPath;
        if (query.Count > 0)
        {
            var separator = renderedPath.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            pathWithQuery = renderedPath + separator + string.Join("&", query);
        }

        return new Uri(baseUri, pathWithQuery);
    }

    private Uri ResolveBaseUri(IReadOnlyDictionary<string, string> settings)
    {
        if (settings.TryGetValue(BaseUrlSettingKey, out var raw) &&
            !string.IsNullOrWhiteSpace(raw))
        {
            if (Uri.TryCreate(raw, UriKind.Absolute, out var configured))
            {
                return EnsureTrailingSlash(configured);
            }

            throw new ArgumentException(
                $"Setting {BaseUrlSettingKey} is not a valid absolute URL: {raw}.",
                nameof(settings));
        }

        if (!Uri.TryCreate(definition.Links[0], UriKind.Absolute, out var fallback))
        {
            throw new InvalidOperationException($"Cardigann definition {definition.Id} has no usable base URL.");
        }

        return EnsureTrailingSlash(fallback);
    }
}
