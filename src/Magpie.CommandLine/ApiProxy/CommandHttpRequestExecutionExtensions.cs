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
        bool blob,
        string? outputPath,
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
                var completionOption = blob
                    ? HttpCompletionOption.ResponseHeadersRead
                    : HttpCompletionOption.ResponseContentRead;
                using var response = await client
                    .SendAsync(request, completionOption, cancellationToken)
                    .ConfigureAwait(false);
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

                if (!blob)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        await output.WriteLineAsync(FormatJson(body)).ConfigureAwait(false);
                    }

                    return 0;
                }

                var targetPath = string.IsNullOrWhiteSpace(outputPath)
                    ? ResolveSuggestedFileName(response)
                    : outputPath;
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    await error.WriteLineAsync("The server did not suggest a file name; specify --output.")
                        .ConfigureAwait(false);
                    return 1;
                }

                var fullPath = Path.GetFullPath(targetPath);
                var file = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await using (file.ConfigureAwait(false))
                {
                    await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                }

                await output.WriteLineAsync(fullPath).ConfigureAwait(false);
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

    private static string? ResolveSuggestedFileName(HttpResponseMessage response)
    {
        var contentDisposition = response.Content.Headers.ContentDisposition;
        var fileName = contentDisposition?.FileNameStar ?? contentDisposition?.FileName;
        var name = Path.GetFileName(fileName?.Trim('"').Replace('\\', '/'));
        return string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(':')
                ? null
                : name;
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
