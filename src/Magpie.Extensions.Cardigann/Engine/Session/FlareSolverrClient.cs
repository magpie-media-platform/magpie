using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Sdk.Caching;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Cardigann.Engine.Session;

internal sealed class FlareSolverrClient(
    IHttpClientFactory clientFactory,
    ISettingsService settingsService,
    ICacheFactory cacheFactory,
    ILogger<FlareSolverrClient> logger,
    SolverQueue queue) : ICloudflareSolver
{
    public const string HttpClientName = "magpie.cardigann.flaresolverr";
    public const string SettingsGroupName = "magpie.cardigann";
    public const string UrlSettingKey = "flareSolverrUrl";
    public const string MemoName = "cardigann-flaresolverr";

    private const string EndpointSuffix = "/v1";
    private const int SolverTimeoutMilliseconds = 60000;

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(65);

    public static readonly TimeSpan ReuseWindow = TimeSpan.FromSeconds(60);

    private readonly ICache _memo = cacheFactory.GetOrCreate(
        MemoName,
        () => new CachePolicy
        {
            Freshness = ReuseWindow,
            Retention = ReuseWindow,
            MaximumEntries = 64,
        });

    private static HttpContent BuildCommand(Uri target)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("cmd", "request.get");
            writer.WriteString("url", target.AbsoluteUri);
            writer.WriteNumber("maxTimeout", SolverTimeoutMilliseconds);
            writer.WriteEndObject();
        }

        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    public async Task<CloudflareClearance?> SolveAsync(
        Uri target, bool reuse, CancellationToken cancellationToken)
    {
        var endpoint = await ResolveEndpointAsync(cancellationToken).ConfigureAwait(false);
        if (endpoint is null)
        {
            return null;
        }

        using var activity = Telemetry.Source.StartActivity("cardigann.cloudflare.solve");
        activity?.SetTag("magpie.request.role", "cloudflareClearance");
        activity?.SetTag("magpie.request.target", target.GetLeftPart(UriPartial.Authority));

        var outcome = "unreachable";
        try
        {
            if (reuse && TryReuse(target) is { } remembered)
            {
                outcome = "reused";
                return remembered with { Reused = true };
            }

            if (!await queue.EnterAsync(cancellationToken).ConfigureAwait(false))
            {
                outcome = "busy";
                logger.LogWarning(
                    "FlareSolverr at {Endpoint} was busy for {QueueWait}, so the challenge of {Target} stands",
                    endpoint,
                    queue.Wait,
                    target.GetLeftPart(UriPartial.Authority));
                return null;
            }

            try
            {
                if (reuse && TryReuse(target) is { } shared)
                {
                    outcome = "reused";
                    return shared with { Reused = true };
                }

                string payload;
                using (var content = BuildCommand(target))
                {
                    using var response = await clientFactory
                        .CreateClient(HttpClientName)
                        .PostAsync(endpoint, content, cancellationToken)
                        .ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        outcome = "rejected";
                        logger.LogWarning(
                            "FlareSolverr at {Endpoint} returned HTTP {Status}",
                            endpoint,
                            (int)response.StatusCode);
                        return null;
                    }

                    payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }

                var clearance = ParseClearance(payload, endpoint);
                outcome = clearance is null ? "rejected" : "solved";
                if (reuse && clearance is not null)
                {
                    Remember(target, clearance);
                }

                return clearance;
            }
            finally
            {
                queue.Leave();
            }
        }
        catch (JsonException exception)
        {
            outcome = "rejected";
            logger.LogWarning(exception, "FlareSolverr at {Endpoint} returned a body that is not JSON", endpoint);
            return null;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "FlareSolverr at {Endpoint} could not be reached", endpoint);
            return null;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "FlareSolverr at {Endpoint} did not answer in time", endpoint);
            return null;
        }
        finally
        {
            activity?.SetTag("magpie.request.outcome", outcome);
            Telemetry.Solves.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        }
    }

    private static string MemoKey(Uri target) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target.AbsoluteUri)));

    public void Forget(Uri target)
    {
        _memo.Remove(MemoKey(target));
    }

    private CloudflareClearance? TryReuse(Uri target)
    {
        return _memo.TryGet<CloudflareClearance>(MemoKey(target), out var solution) ? solution : null;
    }

    private void Remember(Uri target, CloudflareClearance solution)
    {
        _memo.Set(MemoKey(target), solution);
    }

    private CloudflareClearance? ParseClearance(string payload, Uri endpoint)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var status = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString()
            : null;
        if (!string.Equals(status, "ok", StringComparison.Ordinal) ||
            !root.TryGetProperty("solution", out var solution))
        {
            logger.LogWarning(
                "FlareSolverr at {Endpoint} did not solve the challenge and reported status {Status}.",
                endpoint,
                status ?? "none");
            return null;
        }

        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        if (solution.TryGetProperty("cookies", out var cookieArray) && cookieArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var cookie in cookieArray.EnumerateArray())
            {
                if (cookie.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } cookieName)
                {
                    cookies[cookieName] = cookie.TryGetProperty("value", out var value)
                        ? value.GetString() ?? string.Empty
                        : string.Empty;
                }
            }
        }

        var solvedBody = solution.TryGetProperty("response", out var solvedResponse)
            ? solvedResponse.GetString()
            : null;
        if (cookies.Count == 0 && string.IsNullOrEmpty(solvedBody))
        {
            logger.LogWarning(
                "FlareSolverr at {Endpoint} returned a solution that carries neither cookies nor a page",
                endpoint);
            return null;
        }

        return new CloudflareClearance
        {
            Cookies = cookies,
            UserAgent = solution.TryGetProperty("userAgent", out var userAgent) ? userAgent.GetString() : null,
            Body = solvedBody,
            Status = solution.TryGetProperty("status", out var solvedStatus) &&
                solvedStatus.TryGetInt32(out var parsedStatus)
                    ? parsedStatus
                    : 0,
        };
    }

    private async Task<Uri?> ResolveEndpointAsync(CancellationToken cancellationToken)
    {
        var detail = await settingsService
            .GetExtensionGroupAsync(Constants.ExtensionId, SettingsGroupName, cancellationToken)
            .ConfigureAwait(false);
        var configured = string.Empty;
        if (detail is not null)
        {
            foreach (var setting in detail.Group.Settings)
            {
                if (string.Equals(setting.Key, UrlSettingKey, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(setting.Value))
                {
                    configured = setting.Value.Trim();
                }
            }
        }

        if (configured.Length == 0)
        {
            return null;
        }

        var normalized = configured.TrimEnd('/');
        if (!normalized.EndsWith(EndpointSuffix, StringComparison.OrdinalIgnoreCase))
        {
            normalized += EndpointSuffix;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var endpoint) && endpoint.Scheme is "http" or "https")
        {
            return endpoint;
        }

        logger.LogWarning(
            "The Cardigann {SettingKey} setting is not a valid HTTP URL: {Value}",
            UrlSettingKey,
            configured);
        return null;
    }
}
