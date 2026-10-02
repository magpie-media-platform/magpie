using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace Magpie.CommandLine.Dispatch;

internal static class CommandTransportFactory
{
    private const string ApiKeyHeader = "X-Api-Key";

    public static async Task<HttpClient> CreateAsync(
        CommandTransportOptions options,
        Func<CancellationToken, Task<WebApplication>> localAppFactory,
        CancellationToken cancellationToken)
    {
        if (options.IsUrlExplicit && string.IsNullOrWhiteSpace(options.Url))
        {
            throw new CommandTargetResolutionException("Remote URL from --url must not be blank.");
        }

        var apiKeyWasExplicit = options.IsApiKeyExplicit || !string.IsNullOrWhiteSpace(options.ApiKey);
        if (string.IsNullOrWhiteSpace(options.Url))
        {
            if (apiKeyWasExplicit)
            {
                throw new CommandTargetResolutionException("Remote commands require --url when --api-key is supplied.");
            }

            var app = await localAppFactory(cancellationToken).ConfigureAwait(false);

            return app.GetTestClient();
        }

        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            throw new CommandTargetResolutionException(
                $"Remote URL {options.Url} is not an absolute http or https URL.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new CommandTargetResolutionException("Remote commands using --url require --api-key.");
        }

        var client = new HttpClient { BaseAddress = NormalizeBaseUrl(url) };
        client.DefaultRequestHeaders.TryAddWithoutValidation(ApiKeyHeader, options.ApiKey);

        return client;
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
}
