using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Sdk.Caching;
using Magpie.Sdk.Http;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Cardigann.Engine.Session;

internal sealed class FlareSolverrClient(
    IHttpClientFactory clientFactory,
    ISettingsService settingsService,
    ICacheFactory cacheFactory,
    ILogger<FlareSolverrClient> logger,
    IUpstreamCooldown cooldown,
    SolverQueue queue,
    IMagpieSystem magpieSystem,
    TimeProvider timeProvider) : ICloudflareSolver
{
    public const string HttpClientName = "magpie.cardigann.flaresolverr";
    public const string SettingsGroupName = "magpie.cardigann";
    public const string UrlSettingKey = "flareSolverrUrl";
    public const string MemoName = "cardigann-flaresolverr";
    public const string FailureMemoName = "cardigann-flaresolverr-failures";

    private const string EndpointSuffix = "/v1";
    private const int SolverTimeoutMilliseconds = 60000;
    private const int SessionTtlMinutes = 60;

    public static readonly TimeSpan ReuseWindow = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(10);

    private readonly ICache _memo = cacheFactory.GetOrCreate(
        MemoName,
        () => new CachePolicy
        {
            Freshness = ReuseWindow,
            Retention = ReuseWindow,
            MaximumEntries = 64,
        });

    private readonly ICache _failures = cacheFactory.GetOrCreate(
        FailureMemoName,
        () => new CachePolicy
        {
            Freshness = FailureBackoff,
            Retention = FailureBackoff,
            MaximumEntries = 64,
        });

    private static HttpContent BuildCommand(string command, string session, Uri? target = null)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("cmd", command);
            writer.WriteString("session", session);
            if (target is not null)
            {
                writer.WriteString("url", target.AbsoluteUri);
                writer.WriteNumber("maxTimeout", SolverTimeoutMilliseconds);
                writer.WriteNumber("session_ttl_minutes", SessionTtlMinutes);
            }

            writer.WriteEndObject();
        }

        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    public async Task<CloudflareClearance?> SolveAsync(
        Uri target,
        bool reuse,
        CancellationToken cancellationToken,
        string? authenticationScope = null)
    {
        var solver = await ResolveSolverAsync(cancellationToken).ConfigureAwait(false);
        if (solver is null)
        {
            return null;
        }

        var (endpoint, session) = solver.Value;
        using var timeout = timeProvider.CreateCancellationTokenSource(
            clientFactory.CreateClient(HttpClientName).Timeout);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var operationToken = lifetime.Token;

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

            if (SolverRequestScope.Current == SolverRequestKind.Background &&
                _failures.TryGet<DateTimeOffset?>(FailureKey(target), out var backoff) && backoff is { } retryAt)
            {
                outcome = "backoff";
                logger.LogInformation(
                    "FlareSolverr skipped the challenge of {Target} for {Remaining} because it failed there recently",
                    target.GetLeftPart(UriPartial.Authority),
                    retryAt - timeProvider.GetUtcNow());
                return null;
            }

            if (!await queue.EnterAsync(operationToken).ConfigureAwait(false))
            {
                outcome = "busy";
                logger.LogWarning(
                    "FlareSolverr at {Endpoint} was busy for {QueueWait} for the challenge of {Target}",
                    endpoint,
                    queue.Wait,
                    target.GetLeftPart(UriPartial.Authority));
                throw new SolverBusyException();
            }

            var solving = SolveAdmittedAsync();
            (CloudflareClearance? Clearance, string Outcome) solved;
            try
            {
                solved = await solving.WaitAsync(operationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
            {
                outcome = "cancelled";
                _ = solving.ContinueWith(LogAbandoned, target.GetLeftPart(UriPartial.Authority),
                    CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                throw;
            }

            outcome = solved.Outcome;
            return solved.Clearance;
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
            outcome = "timeout";
            logger.LogWarning(exception, "FlareSolverr at {Endpoint} did not answer in time", endpoint);
            return null;
        }
        finally
        {
            activity?.SetTag("magpie.request.outcome", outcome);
            Telemetry.Solves.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        }

        async Task<(CloudflareClearance? Clearance, string Outcome)> SolveAdmittedAsync()
        {
            var sending = false;
            try
            {
                if (reuse && TryReuse(target) is { } shared)
                {
                    return (shared with { Reused = true }, "reused");
                }

                CloudflareClearance? clearance = null;
                var challengeFailed = false;
                var solverFailed = false;
                using var request = new HttpRequestMessage(HttpMethod.Get, target);
                using var admitted = await cooldown.SendAsync(request, authenticationScope, async token =>
                {
                    token.ThrowIfCancellationRequested();
                    sending = true;
                    using var content = BuildCommand("request.get", session, target);
                    using var response = await clientFactory
                        .CreateClient(HttpClientName)
                        .PostAsync(endpoint, content, CancellationToken.None)
                        .ConfigureAwait(false);
                    var payload = await response.Content.ReadAsStringAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    sending = false;
                    if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                        response.StatusCode == HttpStatusCode.ServiceUnavailable &&
                        response.Headers.Contains("Retry-After"))
                    {
                        var throttled = new HttpResponseMessage(response.StatusCode);
                        if (response.Headers.TryGetValues("Retry-After", out var retryAfter))
                        {
                            throttled.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
                        }

                        return throttled;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        solverFailed = true;
                        challengeFailed = IsChallengeFailure(payload);
                        logger.LogWarning(
                            "FlareSolverr at {Endpoint} returned HTTP {Status}",
                            endpoint,
                            (int)response.StatusCode);
                        return new HttpResponseMessage(HttpStatusCode.OK);
                    }

                    using var document = JsonDocument.Parse(payload);
                    if (document.RootElement.ValueKind == JsonValueKind.Object &&
                        document.RootElement.TryGetProperty("solution", out var solution) &&
                        solution.ValueKind == JsonValueKind.Object &&
                        solution.TryGetProperty("status", out var status) && status.TryGetInt32(out var code) &&
                        code is 429 or 503)
                    {
                        var throttled = new HttpResponseMessage((HttpStatusCode)code);
                        if (solution.TryGetProperty("headers", out var headers) &&
                            headers.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var header in headers.EnumerateObject())
                            {
                                if (header.Name.Equals("Retry-After", StringComparison.OrdinalIgnoreCase) &&
                                    header.Value.ValueKind == JsonValueKind.String)
                                {
                                    throttled.Headers.TryAddWithoutValidation("Retry-After", header.Value.GetString());
                                }
                            }
                        }

                        if (code == 429 || throttled.Headers.Contains("Retry-After"))
                        {
                            return throttled;
                        }

                        throttled.Dispose();
                    }

                    clearance = ParseClearance(payload, endpoint);
                    challengeFailed = clearance is null && IsChallengeFailure(payload);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }, operationToken).ConfigureAwait(false);

                if (challengeFailed)
                {
                    var until = timeProvider.GetUtcNow() + FailureBackoff;
                    _failures.Set<DateTimeOffset?>(FailureKey(target), until);
                    logger.LogWarning(
                        "FlareSolverr could not solve the challenge of {Target}, so background solves " +
                        "skip it for {Backoff}",
                        target.GetLeftPart(UriPartial.Authority),
                        FailureBackoff);
                }
                else if (reuse && clearance is not null)
                {
                    Remember(target, clearance);
                }

                if (solverFailed)
                {
                    await DestroySessionAsync(endpoint, session).ConfigureAwait(false);
                }

                return (clearance, clearance is null ? "rejected" : "solved");
            }
            catch (TaskCanceledException) when (sending)
            {
                await DestroySessionAsync(endpoint, session).ConfigureAwait(false);
                throw;
            }
            finally
            {
                queue.Leave();
            }
        }
    }

    private static string FailureKey(Uri target) => target.GetLeftPart(UriPartial.Authority).ToUpperInvariant();

    private static bool IsChallengeFailure(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String &&
                message.GetString() is { } text &&
                (text.Contains("Error solving the challenge. Timeout after", StringComparison.Ordinal) ||
                    text.Contains("Cloudflare has blocked this request", StringComparison.Ordinal));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string MemoKey(Uri target) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target.AbsoluteUri)));

    private async Task DestroySessionAsync(Uri endpoint, string session)
    {
        try
        {
            using var timeout = timeProvider.CreateCancellationTokenSource(
                clientFactory.CreateClient(HttpClientName).Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            using var response = await cooldown.SendAsync(request, null, async token =>
            {
                using var content = BuildCommand("sessions.destroy", session);
                return await clientFactory.CreateClient(HttpClientName).PostAsync(endpoint, content, token)
                    .ConfigureAwait(false);
            }, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "FlareSolverr at {Endpoint} did not destroy session {Session} and returned HTTP {Status}",
                    endpoint,
                    session,
                    (int)response.StatusCode);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogDebug(
                exception,
                "FlareSolverr at {Endpoint} could not be reached to destroy session {Session}",
                endpoint,
                session);
        }
    }

    private void LogAbandoned(Task<(CloudflareClearance? Clearance, string Outcome)> solving, object? target)
    {
        if (solving.IsCanceled)
        {
            return;
        }

        if (solving.Exception is { } exception)
        {
            logger.LogWarning(
                exception.GetBaseException(),
                "FlareSolverr failed the challenge of {Target} after its caller left",
                target);
            return;
        }

        logger.LogInformation(
            "FlareSolverr finished the challenge of {Target} with {Outcome} after its caller left",
            target,
            solving.Result.Outcome);
    }

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

    private async Task<(Uri Endpoint, string Session)?> ResolveSolverAsync(CancellationToken cancellationToken)
    {
        var detail = await settingsService
            .GetExtensionGroupAsync(Constants.ExtensionId, SettingsGroupName, cancellationToken)
            .ConfigureAwait(false);
        if (detail is null)
        {
            return null;
        }

        var configured = string.Empty;
        foreach (var setting in detail.Group.Settings)
        {
            if (string.Equals(setting.Key, UrlSettingKey, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(setting.Value))
            {
                configured = setting.Value.Trim();
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
            return (endpoint, $"magpie-{detail.Group.Id:N}-{magpieSystem.NodeId}");
        }

        logger.LogWarning(
            "The Cardigann {SettingKey} setting is not a valid HTTP URL: {Value}",
            UrlSettingKey,
            configured);
        return null;
    }
}
