using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary.QueryEngine;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.OpenLibrary;

internal sealed class OpenLibraryClient(
    IHttpClientFactory httpClientFactory,
    ILogger<OpenLibraryClient> logger) : IOpenLibraryClient
{
    public const string HttpClientName = "magpie.openlibrary";

    public const string DefaultBaseUrl = "https://openlibrary.org/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<T?> GetJsonAsync<T>(
        ApiRequest request,
        RequestContext context,
        CancellationToken cancellationToken)
        where T : class
    {
        using var response = await SendAsync(request, context, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound &&
            request.Kind is ApiRequestKind.Author or ApiRequestKind.Edition)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        cancellationToken.ThrowIfCancellationRequested();
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("Open Library returned an empty JSON response.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                ?? throw new InvalidOperationException("Open Library returned an empty JSON document.");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Open Library returned malformed JSON for request kind {RequestKind}", request.Kind);
            throw new InvalidOperationException("Open Library returned malformed JSON.", ex);
        }
    }

    public async Task<HttpStatusCode> GetStatusAsync(
        ApiRequest request,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, context, cancellationToken).ConfigureAwait(false);
        return response.StatusCode;
    }

    private Uri BuildUri(ApiRequest request)
    {
        var builder = new StringBuilder(DefaultBaseUrl.TrimEnd('/'));
        builder.Append('/').Append(request.Path.TrimStart('/'));
        if (request.Parameters.Count == 0)
        {
            return new Uri(builder.ToString(), UriKind.Absolute);
        }

        builder.Append('?');
        for (var index = 0; index < request.Parameters.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('&');
            }

            var parameter = request.Parameters[index];
            builder.Append(Uri.EscapeDataString(parameter.Key))
                .Append('=')
                .Append(Uri.EscapeDataString(parameter.Value));
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private async Task<HttpResponseMessage> SendAsync(
        ApiRequest request,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, BuildUri(request));
        message.Headers.TryAddWithoutValidation("User-Agent", context.UserAgent);
        message.Headers.TryAddWithoutValidation("Accept", "application/json");
        var http = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            return await http.SendAsync(
                message,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Open Library request timed out.", ex);
        }
    }
}
