using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.CommandLine.Dispatch;

internal sealed class RemoteCommandTransport(HttpClient client, string? apiKey) : ICommandTransport
{
    private const string ApiKeyHeader = "X-Api-Key";

    private readonly HttpClient _client = client;

    internal RemoteCommandTransport(Uri baseUrl, string? apiKey)
        : this(new HttpClient { BaseAddress = NormalizeBaseUrl(baseUrl) }, apiKey)
    {
    }

    private static Uri NormalizeBaseUrl(Uri baseUrl)
    {
        var text = baseUrl.ToString();
        if (!text.EndsWith("/", StringComparison.Ordinal))
        {
            text += "/";
        }

        return new Uri(text, UriKind.Absolute);
    }

    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.Remove(ApiKeyHeader);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation(ApiKeyHeader, apiKey);
        }

        return _client.SendAsync(request, completionOption, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();

        return ValueTask.CompletedTask;
    }
}
