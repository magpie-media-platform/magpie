using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz.Mapping;
using Magpie.Extensions.MusicBrainz.QueryEngine;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.MusicBrainz;

internal sealed class MusicBrainzClient(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    ILogger<MusicBrainzClient> logger) : IMusicBrainzClient
{
    public const string HttpClientName = "magpie.musicbrainz";

    public const string DefaultBaseUrl = "https://musicbrainz.org/ws/2/";

    private const string DefaultContact = "https://github.com/magpie-app/magpie";
    private const int MaximumIsrcResponseBytes = 8 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MaximumRequestInterval = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _requestGate = new(1, 1);

    private long _nextSendTimestamp;

    public async Task<FetchResult<SearchPage>> SearchAsync(
        Request request,
        Settings settings,
        CancellationToken cancellationToken)
    {
        switch (request.Target)
        {
            case QueryTarget.Artist:
                var artists = await SendJsonAsync<ArtistSearchResponseDto>(
                    request.RelativeTarget,
                    "artist-search",
                    request.Fingerprint,
                    settings,
                    maximumBytes: null,
                    cancellationToken).ConfigureAwait(false);
                return Transform(
                    artists,
                    response => new SearchPage
                    {
                        Count = response.Count,
                        Offset = response.Offset,
                        Artists = response.Artists ?? []
                    });
            case QueryTarget.Album when request.ReleaseIndex:
                var releases = await SendJsonAsync<ReleaseSearchResponseDto>(
                    request.RelativeTarget,
                    "release-search",
                    request.Fingerprint,
                    settings,
                    maximumBytes: null,
                    cancellationToken).ConfigureAwait(false);
                return Transform(
                    releases,
                    response => new SearchPage
                    {
                        Count = response.Count,
                        Offset = response.Offset,
                        Releases = response.Releases ?? []
                    });
            case QueryTarget.Album:
                var releaseGroups = await SendJsonAsync<ReleaseGroupSearchResponseDto>(
                    request.RelativeTarget,
                    "release-group-search",
                    request.Fingerprint,
                    settings,
                    maximumBytes: null,
                    cancellationToken).ConfigureAwait(false);
                return Transform(
                    releaseGroups,
                    response => new SearchPage
                    {
                        Count = response.Count,
                        Offset = response.Offset,
                        ReleaseGroups = response.ReleaseGroups ?? []
                    });
            case QueryTarget.Track:
                var recordings = await SendJsonAsync<RecordingSearchResponseDto>(
                    request.RelativeTarget,
                    "recording-search",
                    request.Fingerprint,
                    settings,
                    maximumBytes: null,
                    cancellationToken).ConfigureAwait(false);
                return Transform(
                    recordings,
                    response => new SearchPage
                    {
                        Count = response.Count,
                        Offset = response.Offset,
                        Recordings = response.Recordings ?? []
                    });
            default:
                return new FetchResult<SearchPage> { Outcome = FetchOutcome.InvalidRequest };
        }
    }

    public async Task<FetchResult<EntityResponseDto>> GetEntityAsync(
        EntityCacheKey key,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var relativeTarget = key.RequestTarget;
        switch (key.Kind)
        {
            case EntityKind.Artist:
                var artist = await SendJsonAsync<ArtistDto>(
                    relativeTarget,
                    "artist-lookup",
                    DiagnosticIdentity(key.Mbid),
                    settings,
                    maximumBytes: null,
                    cancellationToken).ConfigureAwait(false);
                return Transform(artist, value => new EntityResponseDto { Artist = value });
            case EntityKind.ReleaseGroup:
                var releaseGroup = await SendJsonAsync<ReleaseGroupDto>(
                    relativeTarget,
                    "release-group-lookup",
                    DiagnosticIdentity(key.Mbid),
                    settings,
                    maximumBytes: null,
                    cancellationToken).ConfigureAwait(false);
                return Transform(releaseGroup, value => new EntityResponseDto { ReleaseGroup = value });
            case EntityKind.Release:
                var release = await SendJsonAsync<ReleaseDto>(
                    relativeTarget,
                    "release-lookup",
                    DiagnosticIdentity(key.Mbid),
                    settings,
                    maximumBytes: null,
                    cancellationToken).ConfigureAwait(false);
                return Transform(release, value => new EntityResponseDto { Release = value });
            case EntityKind.Recording:
                var recording = await SendJsonAsync<RecordingDto>(
                    relativeTarget,
                    "recording-lookup",
                    DiagnosticIdentity(key.Mbid),
                    settings,
                    maximumBytes: null,
                    cancellationToken).ConfigureAwait(false);
                return Transform(recording, value => new EntityResponseDto { Recording = value });
            case EntityKind.Label:
                var label = await SendJsonAsync<LabelDto>(
                    relativeTarget,
                    "label-lookup",
                    DiagnosticIdentity(key.Mbid),
                    settings,
                    maximumBytes: null,
                    cancellationToken).ConfigureAwait(false);
                return Transform(label, value => new EntityResponseDto { Label = value });
            default:
                return new FetchResult<EntityResponseDto> { Outcome = FetchOutcome.InvalidRequest };
        }
    }

    public Task<FetchResult<ReleaseGroupSearchResponseDto>> BrowseReleaseGroupsAsync(
        BrowseCacheKey key,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var target = string.Format(
            CultureInfo.InvariantCulture,
            "release-group?fmt=json&artist={0}&release-group-status={1}&inc=genres&limit={2}&offset={3}",
            Uri.EscapeDataString(key.ArtistMbid),
            Uri.EscapeDataString(key.StatusFilter),
            key.Limit,
            key.Offset);
        return SendJsonAsync<ReleaseGroupSearchResponseDto>(
            target,
            "release-group-browse",
            DiagnosticIdentity(key.ArtistMbid),
            settings,
            maximumBytes: null,
            cancellationToken);
    }

    public Task<FetchResult<ArtistBrowseResponseDto>> BrowseArtistsAsync(
        ArtistBrowseCacheKey key,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var target = key.RequestTarget;
        if (target is null)
        {
            return Task.FromResult(new FetchResult<ArtistBrowseResponseDto> { Outcome = FetchOutcome.InvalidRequest });
        }

        return SendJsonAsync<ArtistBrowseResponseDto>(
            target,
            "artist-browse",
            DiagnosticIdentity(key.RelatedMbid),
            settings,
            maximumBytes: null,
            cancellationToken);
    }

    public async Task<FetchResult<ArtistResolutionResponse>>
        ResolveArtistCompositionAsync(
            ArtistResolutionRequest request,
            Settings settings,
            CancellationToken cancellationToken)
    {
        switch (request.Entity)
        {
            case ArtistCompositionEntity.Artist:
                var artists = await SendJsonAsync<ArtistSearchResponseDto>(
                    request.RelativeTarget,
                    "artist-composition-resolution",
                    request.Fingerprint,
                    settings,
                    maximumBytes: null,
                    cancellationToken).ConfigureAwait(false);
                return Transform(
                    artists,
                    response => ResolutionResponse(
                        response.Count,
                        response.Artists?.Select(value => (value.Id, value.Name, value.Type, value.Aliases)) ?? []));
            case ArtistCompositionEntity.ReleaseGroup:
                var releaseGroups = await SendJsonAsync<ReleaseGroupSearchResponseDto>(
                    request.RelativeTarget,
                    "artist-composition-resolution",
                    request.Fingerprint,
                    settings,
                    maximumBytes: null,
                    cancellationToken).ConfigureAwait(false);
                return Transform(
                    releaseGroups,
                    response => ResolutionResponse(
                        response.Count,
                        response.ReleaseGroups?.Select(value =>
                            (value.Id, value.Title, (string?)null, (IReadOnlyList<AliasDto>?)null)) ??
                        []));
            case ArtistCompositionEntity.Release:
                var releases = await SendJsonAsync<ReleaseSearchResponseDto>(
                    request.RelativeTarget,
                    "artist-composition-resolution",
                    request.Fingerprint,
                    settings,
                    maximumBytes: null,
                    cancellationToken).ConfigureAwait(false);
                return Transform(
                    releases,
                    response => ResolutionResponse(
                        response.Count,
                        response.Releases?.Select(value =>
                            (value.Id, value.Title, (string?)null, (IReadOnlyList<AliasDto>?)null)) ??
                        []));
            case ArtistCompositionEntity.Recording:
                var recordings = await SendJsonAsync<RecordingSearchResponseDto>(
                    request.RelativeTarget,
                    "artist-composition-resolution",
                    request.Fingerprint,
                    settings,
                    maximumBytes: null,
                    cancellationToken).ConfigureAwait(false);
                return Transform(
                    recordings,
                    response => ResolutionResponse(
                        response.Count,
                        response.Recordings?.Select(value =>
                            (value.Id, value.Title, (string?)null, (IReadOnlyList<AliasDto>?)null)) ??
                        []));
            case ArtistCompositionEntity.Work:
                var works = await SendJsonAsync<WorkSearchResponseDto>(
                    request.RelativeTarget,
                    "artist-composition-resolution",
                    request.Fingerprint,
                    settings,
                    maximumBytes: null,
                    cancellationToken).ConfigureAwait(false);
                return Transform(
                    works,
                    response => ResolutionResponse(
                        response.Count,
                        response.Works?.Select(value =>
                            (value.Id, value.Title, (string?)null, (IReadOnlyList<AliasDto>?)null)) ??
                        []));
            default:
                return new FetchResult<ArtistResolutionResponse> { Outcome = FetchOutcome.InvalidRequest };
        }
    }

    private static ArtistResolutionResponse ResolutionResponse(
        int count,
        IEnumerable<(
            string? Mbid,
            string? Name,
            string? ArtistType,
            IReadOnlyList<AliasDto>? Aliases)> candidates)
    {
        var returned = candidates.ToArray();
        return new ArtistResolutionResponse
        {
            Count = count,
            ReturnedCount = returned.Length,
            Candidates = [.. returned
                .Where(value => Guid.TryParse(value.Mbid, out _) && !string.IsNullOrWhiteSpace(value.Name))
                .Select(value => new ArtistResolutionCandidate
                {
                    Mbid = Guid.Parse(value.Mbid!).ToString("D", CultureInfo.InvariantCulture),
                    Name = value.Name!,
                    Aliases = [.. (value.Aliases ?? [])
                        .Select(alias => alias.Name)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Cast<string>()],
                    ArtistType = value.ArtistType
                })]
        };
    }

    public async Task<FetchResult<SearchPage>> GetIsrcAsync(
        string isrc,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var response = await SendJsonAsync<RecordingSearchResponseDto>(
            "isrc/" + Uri.EscapeDataString(isrc) + "?fmt=json&inc=artist-credits",
            "isrc-lookup",
            DiagnosticIdentity(isrc),
            settings,
            MaximumIsrcResponseBytes,
            cancellationToken).ConfigureAwait(false);
        return Transform(
            response,
            value => new SearchPage
            {
                Count = value.Count > 0 ? value.Count : value.Recordings?.Count ?? 0,
                Offset = value.Offset,
                Recordings = [.. (value.Recordings ?? []).Select(recording => recording with
                {
                    Isrcs =
                    [
                        .. (recording.Isrcs ?? [])
                            .Append(value.Isrc ?? isrc)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                    ]
                })]
            });
    }

    private static FetchResult<TOut> Transform<TIn, TOut>(FetchResult<TIn> source, Func<TIn, TOut> transform)
        where TIn : class
        where TOut : class
    {
        return source is { Outcome: FetchOutcome.Success, Value: { } value }
            ? new FetchResult<TOut>
            {
                Outcome = FetchOutcome.Success,
                Value = transform(value),
                StatusCode = source.StatusCode
            }
            : new FetchResult<TOut> { Outcome = source.Outcome, StatusCode = source.StatusCode };
    }

    private static string BuildUserAgent(Settings settings)
    {
        var contact = string.IsNullOrWhiteSpace(settings.Contact) ? DefaultContact : settings.Contact!.Trim();
        var version = typeof(MusicBrainzClient).Assembly.GetName().Version ?? new Version(1, 0, 0);
        return string.Format(
            CultureInfo.InvariantCulture,
            "Magpie/{0} ( {1} )",
            version.ToString(Math.Min(3, version.Build >= 0 ? 3 : 2)),
            contact);
    }

    private static string DiagnosticIdentity(string value)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value));
        return BitConverter.ToString(hash, 0, 6).Replace("-", string.Empty).ToLowerInvariant();
    }

    private TimeSpan RequestInterval(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable))
        {
            return MinimumRequestInterval;
        }

        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta ??
            (retryAfter?.Date is { } date ? date - timeProvider.GetUtcNow() : MinimumRequestInterval);
        if (delay < MinimumRequestInterval)
        {
            return MinimumRequestInterval;
        }

        return delay > MaximumRequestInterval ? MaximumRequestInterval : delay;
    }

    private async Task<FetchResult<T>> SendJsonAsync<T>(
        string relativeTarget,
        string endpoint,
        string diagnosticKey,
        Settings settings,
        int? maximumBytes,
        CancellationToken cancellationToken)
        where T : class
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress ??= new Uri(DefaultBaseUrl, UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(relativeTarget, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", BuildUserAgent(settings));

        HttpResponseMessage response;
        var nextInterval = MinimumRequestInterval;
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var remaining = _nextSendTimestamp - Stopwatch.GetTimestamp();
            if (remaining > 0)
            {
                var pause = TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency);
                await Task.Delay(pause, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("MusicBrainz timed out at {Endpoint} ({Key})", endpoint, diagnosticKey);
                return new FetchResult<T> { Outcome = FetchOutcome.Timeout };
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "MusicBrainz transport failed at {Endpoint} ({Key})", endpoint, diagnosticKey);
                return new FetchResult<T> { Outcome = FetchOutcome.Unavailable };
            }

            nextInterval = RequestInterval(response);
        }
        finally
        {
            _nextSendTimestamp = Stopwatch.GetTimestamp() +
                (long)(nextInterval.TotalSeconds * Stopwatch.Frequency);
            _requestGate.Release();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new FetchResult<T> { Outcome = FetchOutcome.NotFound, StatusCode = (int)response.StatusCode };
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                logger.LogWarning(
                    "MusicBrainz returned {StatusCode} for a builder defect at {Endpoint} ({Key})",
                    (int)response.StatusCode,
                    endpoint,
                    diagnosticKey);
                return new FetchResult<T>
                {
                    Outcome = FetchOutcome.InvalidRequest,
                    StatusCode = (int)response.StatusCode
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "MusicBrainz returned {StatusCode} at {Endpoint} ({Key})",
                    (int)response.StatusCode,
                    endpoint,
                    diagnosticKey);
                return new FetchResult<T>
                {
                    Outcome = FetchOutcome.Unavailable,
                    StatusCode = (int)response.StatusCode
                };
            }

            if (maximumBytes is { } byteLimit && response.Content.Headers.ContentLength is { } contentLength &&
                contentLength > byteLimit)
            {
                return new FetchResult<T>
                {
                    Outcome = FetchOutcome.ResponseTooLarge,
                    StatusCode = (int)response.StatusCode
                };
            }

            try
            {
                if (maximumBytes is { } limit)
                {
                    await response.Content.LoadIntoBufferAsync(limit).ConfigureAwait(false);
                }

                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var value = await JsonSerializer.DeserializeAsync<T>(
                    stream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                return value is null
                    ? new FetchResult<T> { Outcome = FetchOutcome.Malformed }
                    : new FetchResult<T>
                    {
                        Outcome = FetchOutcome.Success,
                        Value = value,
                        StatusCode = (int)response.StatusCode
                    };
            }
            catch (HttpRequestException) when (maximumBytes is not null)
            {
                return new FetchResult<T>
                {
                    Outcome = FetchOutcome.ResponseTooLarge,
                    StatusCode = (int)response.StatusCode
                };
            }
            catch (InvalidOperationException) when (maximumBytes is not null)
            {
                return new FetchResult<T>
                {
                    Outcome = FetchOutcome.ResponseTooLarge,
                    StatusCode = (int)response.StatusCode
                };
            }
            catch (JsonException ex)
            {
                logger.LogWarning(
                    ex,
                    "MusicBrainz returned malformed JSON at {Endpoint} ({Key})",
                    endpoint,
                    diagnosticKey);
                return new FetchResult<T> { Outcome = FetchOutcome.Malformed, StatusCode = (int)response.StatusCode };
            }
        }
    }
}
