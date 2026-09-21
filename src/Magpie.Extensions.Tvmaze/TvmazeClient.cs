using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tvmaze.Mapping;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tvmaze;

internal sealed class TvmazeClient(
    ISettingsService settingsService,
    IHttpClientFactory httpClientFactory,
    ILogger<TvmazeClient> logger) : IClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public const string HttpClientName = "magpie.tvmaze";

    public const string DefaultBaseUrl = "https://api.tvmaze.com/";

    private readonly HttpClient _http = httpClientFactory.CreateClient(HttpClientName);

    private ProviderSettings? _settings;

    private bool _disposed;

    private Uri BuildUri(string path, IReadOnlyList<(string Key, string Value)>? queryParameters)
    {
        var builder = new StringBuilder(DefaultBaseUrl.TrimEnd('/'));
        builder.Append('/').Append(path);
        if (queryParameters is not null && queryParameters.Count > 0)
        {
            var separator = '?';
            for (var i = 0; i < queryParameters.Count; i++)
            {
                var (key, value) = queryParameters[i];
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                builder.Append(separator);
                separator = '&';
                builder.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
            }
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private async Task<ProviderSettings> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        return _settings ??= await settingsService.ResolveTvmazeSettingsAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SearchHitDto>> SearchShowsAsync(
        string searchTerm,
        CancellationToken cancellationToken)
    {
        var hits = await GetJsonAsync<List<SearchHitDto>>(
            BuildUri("search/shows", [("q", searchTerm)]),
            cancellationToken).ConfigureAwait(false);
        return hits ?? throw new HttpRequestException("TVMaze show search was unavailable.");
    }

    public async Task<IReadOnlyList<PersonSearchHitDto>> SearchPeopleAsync(
        string searchTerm,
        CancellationToken cancellationToken)
    {
        var hits = await GetJsonAsync<List<PersonSearchHitDto>>(
            BuildUri("search/people", [("q", searchTerm)]),
            cancellationToken).ConfigureAwait(false);
        return hits ?? throw new HttpRequestException("TVMaze person search was unavailable.");
    }

    public async Task<IReadOnlyList<ShowDto>?> GetShowsPageAsync(int page, CancellationToken cancellationToken)
    {
        return await GetJsonAsync<List<ShowDto>>(
            BuildUri("shows", [("page", page.ToString(CultureInfo.InvariantCulture))]),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PersonDto?> GetPersonByIdAsync(long personId, CancellationToken cancellationToken)
    {
        return await GetJsonAsync<PersonDto>(
            BuildUri(string.Format(CultureInfo.InvariantCulture, "people/{0}", personId), null),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ShowDto?> GetShowWithEmbedsAsync(
        int showId,
        IReadOnlyList<string> embeds,
        CancellationToken cancellationToken)
    {
        var query = embeds.Select(embed => ("embed[]", embed)).ToArray();
        return await GetJsonAsync<ShowDto>(
            BuildUri(string.Format(CultureInfo.InvariantCulture, "shows/{0}", showId), query),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<EpisodeDto?> GetEpisodeAsync(long episodeId, CancellationToken cancellationToken)
    {
        return await GetJsonAsync<EpisodeDto>(
            BuildUri(string.Format(CultureInfo.InvariantCulture, "episodes/{0}", episodeId), null),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SeasonDto>> GetSeasonsAsync(int showId, CancellationToken cancellationToken)
    {
        var seasons = await GetJsonAsync<List<SeasonDto>>(
            BuildUri(string.Format(CultureInfo.InvariantCulture, "shows/{0}/seasons", showId), null),
            cancellationToken).ConfigureAwait(false);
        return seasons ?? [];
    }

    public async Task<IReadOnlyList<CastCreditDto>> GetPersonCastCreditsAsync(
        long personId,
        CancellationToken cancellationToken)
    {
        var credits = await GetJsonAsync<List<CastCreditDto>>(
            BuildUri(
                string.Format(CultureInfo.InvariantCulture, "people/{0}/castcredits", personId),
                [("embed", "show")]),
            cancellationToken).ConfigureAwait(false);
        return credits ?? throw new HttpRequestException("TVMaze cast credits were unavailable.");
    }

    public async Task<IReadOnlyList<CrewCreditDto>> GetPersonCrewCreditsAsync(
        long personId,
        CancellationToken cancellationToken)
    {
        var credits = await GetJsonAsync<List<CrewCreditDto>>(
            BuildUri(
                string.Format(CultureInfo.InvariantCulture, "people/{0}/crewcredits", personId),
                [("embed", "show")]),
            cancellationToken).ConfigureAwait(false);
        return credits ?? throw new HttpRequestException("TVMaze crew credits were unavailable.");
    }

    public async Task<IReadOnlyList<CastEntryDto>> GetEpisodeGuestCastAsync(
        long episodeId,
        CancellationToken cancellationToken)
    {
        var cast = await GetJsonAsync<List<CastEntryDto>>(
            BuildUri(string.Format(CultureInfo.InvariantCulture, "episodes/{0}/guestcast", episodeId), null),
            cancellationToken).ConfigureAwait(false);
        return cast ?? [];
    }

    public async Task<IReadOnlyList<GuestCrewEntryDto>> GetEpisodeGuestCrewAsync(
        long episodeId,
        CancellationToken cancellationToken)
    {
        var crew = await GetJsonAsync<List<GuestCrewEntryDto>>(
            BuildUri(string.Format(CultureInfo.InvariantCulture, "episodes/{0}/guestcrew", episodeId), null),
            cancellationToken).ConfigureAwait(false);
        return crew ?? [];
    }

    public async Task<ShowDto?> LookupShowAsync(
        string externalSource,
        string externalValue,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalSource) || string.IsNullOrWhiteSpace(externalValue))
        {
            return null;
        }

        return await GetJsonFollowingMovedPermanentlyAsync<ShowDto>(
            BuildUri("lookup/shows", [(externalSource, externalValue.Trim())]),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> GetJsonFollowingMovedPermanentlyAsync<T>(Uri uri, CancellationToken cancellationToken)
        where T : class
    {
        var response = await SendAsync(uri, cancellationToken).ConfigureAwait(false);
        try
        {
            if (response.StatusCode == HttpStatusCode.MovedPermanently)
            {
                var location = response.Headers.Location
                    ?? throw new HttpRequestException("TVMaze returned a redirect without a location.");
                var redirectUri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                response.Dispose();
                response = await SendAsync(redirectUri, cancellationToken).ConfigureAwait(false);
            }

            return await ReadJsonAsync<T>(response, uri, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            response.Dispose();
        }
    }

    private async Task<T?> GetJsonAsync<T>(Uri uri, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await SendAsync(uri, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<T>(response, uri, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, Uri uri, CancellationToken cancellationToken)
        where T : class
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        cancellationToken.ThrowIfCancellationRequested();
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                ?? throw new JsonException("TVMaze returned an empty JSON payload.");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "The TVmaze client cannot parse the JSON response for {Uri}.", uri);
            throw new HttpRequestException("TVMaze returned an unusable JSON payload.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        var settings = await ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var userAgent = settings.BuildUserAgent(
            typeof(TvmazeClient).Assembly.GetName().Version ?? new Version(1, 0, 0));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var activity = Telemetry.Source.StartActivity("tvmaze.query.request", ActivityKind.Client);
        activity?.SetTag("magpie.request.target", uri.ToString());
        try
        {
            return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
    }
}
