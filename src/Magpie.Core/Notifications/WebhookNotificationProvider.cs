using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Notifications;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Notifications;

internal sealed class WebhookNotificationProvider(
    IHttpClientFactory httpClientFactory,
    MagpieHostEnvironment hostEnvironment,
    ILogger<WebhookNotificationProvider> logger) : INotificationProvider, IDisposable
{
    private static readonly string MagpieVersion =
        typeof(WebhookNotificationProvider).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(WebhookNotificationProvider).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public const string ProviderTypeName = "webhook";

    public const string HttpClientName = "WebhookNotificationProvider";

    public const string SettingUrl = "url";

    public const string SettingMethod = "method";

    public const string SettingHeaders = "headers";

    public const int MaxConcurrency = 8;

    private readonly SemaphoreSlim _concurrency = new(MaxConcurrency, MaxConcurrency);

    public string ProviderType => ProviderTypeName;

    public IReadOnlyList<SettingSchemaDefinition> SettingsSchema { get; } =
    [
        new()
        {
            Key = SettingUrl,
            Type = DataType.String,
            DisplayName = "URL",
            Description = "The HTTP(S) endpoint the webhook posts to.",
            Required = true,
            Placeholder = "https://example.com/webhook"
        },
        new()
        {
            Key = SettingMethod,
            Type = DataType.String,
            DisplayName = "HTTP method",
            Description = "POST, GET, or PUT. Defaults to POST.",
            DefaultValue = "POST",
            Options = ["POST", "GET", "PUT"]
        },
        new()
        {
            Key = SettingHeaders,
            Type = DataType.String,
            DisplayName = "Headers (JSON)",
            Description = "Additional HTTP headers as a JSON object of name-value pairs.",
            Placeholder = "{\"X-Magpie-Sig\":\"...\"}"
        }
    ];

    private static bool IsTransientStatus(HttpStatusCode statusCode)
    {
        return (int)statusCode >= 500 || statusCode == HttpStatusCode.TooManyRequests;
    }

    private static HttpRequestMessage BuildRequest(
        Uri url,
        string? methodSetting,
        IReadOnlyDictionary<string, string>? headers,
        byte[] payload)
    {
        var method = ParseMethod(methodSetting);
        var request = new HttpRequestMessage(method, url);

        if (method != HttpMethod.Get)
        {
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8"
            };
        }

        if (headers is not null)
        {
            foreach (var (key, value) in headers)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!request.Headers.TryAddWithoutValidation(key, value) && request.Content is not null)
                {
                    request.Content.Headers.TryAddWithoutValidation(key, value);
                }
            }
        }

        return request;
    }

    private static HttpMethod ParseMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            return HttpMethod.Post;
        }

        return method!.Trim().ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "PUT" => HttpMethod.Put,
            "POST" => HttpMethod.Post,
            var other => new HttpMethod(other)
        };
    }

    public async Task SendAsync(
        NotificationRule rule,
        MagpieEvent magpieEvent,
        CancellationToken cancellationToken = default)
    {
        if (!rule.Settings.TryGetValue(SettingUrl, out var urlString) || string.IsNullOrWhiteSpace(urlString))
        {
            logger.LogWarning(
                "Webhook rule {RuleName} ({RuleId}) has no {SettingUrl} setting. The provider skipped the rule",
                rule.Name,
                rule.Id,
                SettingUrl);
            return;
        }

        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var url))
        {
            logger.LogWarning(
                "Webhook rule {RuleName} ({RuleId}) has an invalid URL {Url}. The provider skipped the rule",
                rule.Name,
                rule.Id,
                urlString);
            return;
        }

        rule.Settings.TryGetValue(SettingMethod, out var methodSetting);
        var headers = ParseHeaders(rule);

        var payload = SerializePayload(magpieEvent);
        await DispatchAsync(rule.Name, rule.Id, url, methodSetting, headers, payload, magpieEvent, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DispatchAsync(
        string ruleName,
        Guid ruleId,
        Uri url,
        string? methodSetting,
        IReadOnlyDictionary<string, string>? headers,
        byte[] payload,
        MagpieEvent magpieEvent,
        CancellationToken cancellationToken)
    {
        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = BuildRequest(url, methodSetting, headers, payload);

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                if (IsTransientStatus(response.StatusCode))
                {
                    logger.LogWarning(
                        "Webhook rule {RuleName} ({RuleId}) returned transient status {StatusCode} " +
                        "for event {EventName}. The webhook provider will retry",
                        ruleName,
                        ruleId,
                        statusCode,
                        magpieEvent.Name);
                    throw new HttpRequestException(
                        $"Webhook delivery returned transient HTTP status {statusCode} ({response.ReasonPhrase}).");
                }

                logger.LogWarning(
                    "Webhook rule {RuleName} ({RuleId}) returned non-success status {StatusCode} " +
                    "for event {EventName}. The webhook provider dropped the notification and will not retry",
                    ruleName,
                    ruleId,
                    statusCode,
                    magpieEvent.Name);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(
                ex,
                "The webhook request for rule {RuleName} ({RuleId}) timed out for event {EventName}. " +
                "The webhook provider will retry",
                ruleName,
                ruleId,
                magpieEvent.Name);
            throw new HttpRequestException("Webhook delivery timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "The webhook request for rule {RuleName} ({RuleId}) failed for event {EventName}. " +
                "The webhook provider will retry",
                ruleName,
                ruleId,
                magpieEvent.Name);
            throw new HttpRequestException("Webhook delivery failed due to a transport error.", ex);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private byte[] SerializePayload(MagpieEvent magpieEvent)
    {
        var envelope = new WebhookPayload(
            new WebhookPayloadEvent(
                magpieEvent.Name,
                magpieEvent.OccurredAt,
                magpieEvent.Context),
            new WebhookPayloadMagpie(
                string.IsNullOrWhiteSpace(magpieEvent.NodeId) ? hostEnvironment.NodeId : magpieEvent.NodeId,
                MagpieVersion));

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, SerializerOptions));
    }

    private IReadOnlyDictionary<string, string>? ParseHeaders(NotificationRule rule)
    {
        if (!rule.Settings.TryGetValue(SettingHeaders, out var serialized) || string.IsNullOrWhiteSpace(serialized))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(serialized, SerializerOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Webhook rule {RuleName} ({RuleId}) has invalid JSON in {SettingHeaders}. " +
                "The webhook provider ignored the headers",
                rule.Name,
                rule.Id,
                SettingHeaders);
            return null;
        }
    }

    public void Dispose()
    {
        _concurrency.Dispose();
    }

    private sealed record WebhookPayload(WebhookPayloadEvent Event, WebhookPayloadMagpie Magpie);

    private sealed record WebhookPayloadEvent(
        string Name,
        DateTime OccurredAt,
        IReadOnlyDictionary<string, string> Context);

    private sealed record WebhookPayloadMagpie(string NodeId, string Version);
}
