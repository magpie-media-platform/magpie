using System;
using System.Net.Http;

using Magpie.Sdk.Http;

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers shared upstream cooldowns for provider HTTP clients.
/// </summary>
public static class UpstreamHttpClientExtensions
{
    /// <summary>
    /// Waits and retries throttled requests inside the client's other HTTP handlers.
    /// </summary>
    /// <param name="builder">The provider HTTP client registration</param>
    /// <param name="authenticationScope">The stable authentication identity, or null for anonymous access</param>
    /// <param name="followRedirects">Whether to admit redirects when automatic transport redirects are disabled</param>
    /// <param name="requestTimeout">An optional shorter request timeout, including waits and the response body</param>
    /// <returns>The provider HTTP client registration</returns>
    public static IHttpClientBuilder AddUpstreamCooldown(
        this IHttpClientBuilder builder,
        Func<HttpRequestMessage, string?>? authenticationScope = null,
        bool followRedirects = false,
        TimeSpan? requestTimeout = null)
    {
        builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.TryAddSingleton<HttpOptions>();
        builder.Services.TryAddSingleton<IUpstreamCooldown, UpstreamCooldown>();
        return builder.ConfigureHttpClient((services, client) =>
                client.Timeout = services.GetRequiredService<HttpOptions>().Timeout)
            .ConfigureAdditionalHttpMessageHandlers((handlers, services) =>
            handlers.Add(new UpstreamCooldownHandler(
                services.GetRequiredService<IUpstreamCooldown>(),
                services.GetRequiredService<TimeProvider>(),
                authenticationScope,
                followRedirects,
                services.GetRequiredService<HttpOptions>().Timeout,
                requestTimeout)));
    }
}
