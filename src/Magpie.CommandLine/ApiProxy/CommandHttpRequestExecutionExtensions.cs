using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.CommandLine.Dispatch;

using Microsoft.AspNetCore.Builder;

namespace Magpie.CommandLine.ApiProxy;

internal static class CommandHttpRequestExecutionExtensions
{
    private static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public static async Task<int> ExecuteAsync(
        this HttpRequestMessage request,
        CommandTransportOptions transportOptions,
        Func<CancellationToken, Task<WebApplication>> localAppFactory,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        HttpClient client;
        try
        {
            client = await CommandTransportFactory
                .CreateAsync(transportOptions, localAppFactory, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CommandTargetResolutionException ex)
        {
            await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }

        try
        {
            using (client)
            {
                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var failureBody = await response.Content.ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);
                    await error.WriteLineAsync(
                        $"Request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).")
                        .ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(failureBody))
                    {
                        await error.WriteLineAsync(failureBody).ConfigureAwait(false);
                    }

                    return 1;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    await output.WriteLineAsync(FormatJson(body)).ConfigureAwait(false);
                }

                return 0;
            }
        }
        catch (HttpRequestException ex)
        {
            await WriteTransportFailureAsync(ex, error).ConfigureAwait(false);
            return 1;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            await WriteTransportFailureAsync(ex, error).ConfigureAwait(false);
            return 1;
        }
    }

    private static Task WriteTransportFailureAsync(Exception exception, TextWriter error)
    {
        return error.WriteLineAsync($"Request failed: {exception.Message}");
    }

    private static string FormatJson(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(document.RootElement, JsonOptions);
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
