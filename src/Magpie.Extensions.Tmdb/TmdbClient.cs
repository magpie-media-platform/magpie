using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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

    private const int MaximumRequestTargetBytes = 8 * 1024;
    private const int QueryResponseBytes = 8 * 1024 * 1024;
    private const int DetailResponseBytes = 16 * 1024 * 1024;
    private const int ConfigurationResponseBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Task<T?> GetAsync<T>(RequestContext context, Request request, CancellationToken cancellationToken)
        where T : class
    {
        return GetAsync<T>(
            context,
            request.Endpoint,
            request.Path,
            request.QueryParameters,
            request.ResponseProfile,
            cancellationToken);
    }

    public Task<T?> GetAsync<T>(
        RequestContext context,
        string path,
        IReadOnlyList<(string Key, string Value)>? queryParameters,
        ResponseProfile profile,
        CancellationToken cancellationToken)
        where T : class
    {
        return GetAsync<T>(context, RequestRole.Detail, path, queryParameters, profile, cancellationToken);
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

    private async Task<T?> GetAsync<T>(
        RequestContext context,
        string role,
        string path,
        IReadOnlyList<(string Key, string Value)>? queryParameters,
        ResponseProfile profile,
        CancellationToken cancellationToken)
        where T : class
    {
        using var activity = Telemetry.Source.StartChild("tmdb.query.request", context.Telemetry?.Activity);
        if (activity is not null)
        {
            activity.SetTag("magpie.request.role", role);
            activity.SetTag("magpie.request.target", BuildRequestTarget(path, queryParameters, context));
            activity.SetTag("magpie.cache.hit", false);
            if (context.PlanNodeId is { } planNodeId)
            {
                activity.SetTag("magpie.query.node.id", planNodeId);
            }
        }

        Telemetry.Requests.Add(
            1,
            Telemetry.ProviderTag,
            new KeyValuePair<string, object?>("request_role", role),
            Telemetry.CacheMissTag);
        try
        {
            var body = await GetJsonAsync(context, path, queryParameters, profile, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                context.Telemetry?.RecordRequest(0);
                activity?.SetTag("magpie.request.status", "notFound");
                activity?.SetTag("magpie.request.rows", 0);
                activity?.SetTag("magpie.request.payload_bytes", 0);
                return null;
            }

            var result = JsonSerializer.Deserialize<T>(body, JsonOptions);
            var rows = ResponseCount(result);
            context.Telemetry?.RecordRequest(rows);
            activity?.SetTag("magpie.request.status", "complete");
            activity?.SetTag("magpie.request.rows", rows);
            activity?.SetTag("magpie.request.payload_bytes", Encoding.UTF8.GetByteCount(body));
            return result;
        }
        catch (OperationCanceledException)
        {
            activity?.SetTag("magpie.request.status", "canceled");
            throw;
        }
        catch (Exception)
        {
            activity?.SetTag("magpie.request.status", "failed");
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
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
        var http = httpClientFactory.CreateClient(HttpClientName);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound && profile == ResponseProfile.Detail)
            {
                return null;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("TMDb returned HTTP 429 for {Path}", path);
            }

            response.EnsureSuccessStatusCode();

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
