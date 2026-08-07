using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Extensions.Tmdb.QueryEngine;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tmdb;

internal sealed class TmdbClient(IHttpClientFactory httpClientFactory, ILogger<TmdbClient> logger) : ITmdbClient
{
    public const string HttpClientName = "magpie.tmdb";

    public const string DefaultBaseUrl = "https://api.themoviedb.org/";

    public const string DefaultImageBaseUrl = "https://image.tmdb.org/t/p/";

    private const int MaximumRequestTargetBytes = 8 * 1024;
    private const int QueryResponseBytes = 8 * 1024 * 1024;
    private const int DetailResponseBytes = 16 * 1024 * 1024;
    private const int ConfigurationResponseBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex ApiKeyPattern = new(
        @"api_key=[^&\s""'<>]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string ScrubApiKey(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        return ApiKeyPattern.Replace(value!, "api_key=***");
    }

    public Task<T?> GetAsync<T>(RequestContext context, Request request, CancellationToken cancellationToken)
        where T : class
    {
        return GetAsync<T>(context, request.Path, request.QueryParameters, request.ResponseProfile, cancellationToken);
    }

    public async Task<T?> GetAsync<T>(
        RequestContext context,
        string path,
        IReadOnlyList<(string Key, string Value)>? queryParameters,
        ResponseProfile profile,
        CancellationToken cancellationToken)
        where T : class
    {
        var trace = context.ExecutionTrace;
        var traceId = trace?.BeginRequest(
            context.PlanNodeId,
            path,
            ScrubApiKey(BuildRequestTarget(path, queryParameters, context)));
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var body = await GetJsonAsync(context, path, queryParameters, profile, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                if (traceId is { } notFoundId)
                {
                    trace!.CompleteRequest(notFoundId, "notFound", 0, 0, stopwatch.ElapsedMilliseconds);
                }

                return null;
            }

            var result = JsonSerializer.Deserialize<T>(body!, JsonOptions);
            if (traceId is { } completedId)
            {
                trace!.CompleteRequest(
                    completedId,
                    "complete",
                    ResponseCount(result),
                    Encoding.UTF8.GetByteCount(body!),
                    stopwatch.ElapsedMilliseconds);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            if (traceId is { } canceledId)
            {
                trace!.CompleteRequest(canceledId, "canceled", null, 0, stopwatch.ElapsedMilliseconds);
            }

            throw;
        }
        catch (Exception)
        {
            if (traceId is { } failedId)
            {
                trace!.CompleteRequest(failedId, "failed", null, 0, stopwatch.ElapsedMilliseconds);
            }

            throw;
        }
    }

    private static async Task<string> ReadBoundedAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 0 and var length && length > maximumBytes)
        {
            throw new HttpRequestException("TMDb response exceeds the configured body limit.");
        }

        using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new HttpRequestException("TMDb response exceeds the configured body limit.");
            }

            output.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static int ResponseCount(object? result)
    {
        return result switch
        {
            SearchEnvelopeDto<MovieSearchHitDto> envelope => envelope.Results?.Count ?? 0,
            SearchEnvelopeDto<TvSearchHitDto> envelope => envelope.Results?.Count ?? 0,
            SearchEnvelopeDto<NamedSearchHitDto> envelope => envelope.Results?.Count ?? 0,
            PersonMovieCreditsDto credits => (credits.Cast?.Count ?? 0) + (credits.Crew?.Count ?? 0),
            PersonTvCreditsDto credits => (credits.Cast?.Count ?? 0) + (credits.Crew?.Count ?? 0),
            FindEnvelopeDto envelope => (envelope.MovieResults?.Count ?? 0) +
                (envelope.TvResults?.Count ?? 0) +
                (envelope.PersonResults?.Count ?? 0),
            GenreListEnvelopeDto envelope => envelope.Genres?.Count ?? 0,
            null => 0,
            _ => 1
        };
    }

    private static int MaximumResponseBytes(ResponseProfile profile)
    {
        return profile switch
        {
            ResponseProfile.Configuration => ConfigurationResponseBytes,
            ResponseProfile.Detail => DetailResponseBytes,
            _ => QueryResponseBytes
        };
    }

    private static HttpRequestException Sanitized(HttpRequestException exception)
    {
        return new(ScrubApiKey(exception.Message));
    }

    private static string BuildRequestTarget(
        string path,
        IReadOnlyList<(string Key, string Value)>? queryParameters,
        RequestContext context)
    {
        var builder = new StringBuilder(path);
        builder.Append('?').Append("api_key=").Append(Uri.EscapeDataString(context.ApiKey));
        if (queryParameters is not null)
        {
            foreach (var (key, value) in queryParameters)
            {
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                builder.Append('&').Append(Uri.EscapeDataString(key))
                    .Append('=').Append(Uri.EscapeDataString(value));
            }
        }

        return builder.ToString();
    }

    private async Task<string?> GetJsonAsync(
        RequestContext context,
        string path,
        IReadOnlyList<(string Key, string Value)>? queryParameters,
        ResponseProfile profile,
        CancellationToken cancellationToken)
    {
        var requestTarget = BuildRequestTarget(path, queryParameters, context);
        if (Encoding.UTF8.GetByteCount(requestTarget) > MaximumRequestTargetBytes)
        {
            throw new InvalidOperationException("TMDb request target exceeds the 8 KiB limit.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(HttpMethod.Get, requestTarget);
        var response = default(HttpResponseMessage)!;
        try
        {
            var http = httpClientFactory.CreateClient(HttpClientName);
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw Sanitized(ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound && profile == ResponseProfile.Detail)
            {
                return null;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("TMDb rejected the request with HTTP 429 after its resilience retries");
            }

            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                throw Sanitized(ex);
            }

            var body = await ReadBoundedAsync(response, MaximumResponseBytes(profile), cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                throw new HttpRequestException("TMDb returned a malformed empty response body.");
            }

            return body;
        }
    }
}
