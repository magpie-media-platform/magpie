using System;
using System.Net.Http;

using Magpie.Sdk.Http;

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers shared upstream request admission for provider HTTP clients.
/// </summary>
public static class UpstreamHttpClientExtensions
{
    /// <summary>
    /// Adds shared cooldown admission inside the client's other HTTP handlers.
    /// </summary>
    /// <param name="builder">The provider HTTP client registration</param>
    /// <param name="authenticationScope">The stable authentication identity, or null for anonymous access</param>
    /// <param name="followRedirects">Whether to admit redirects when automatic transport redirects are disabled</param>
    /// <returns>The provider HTTP client registration</returns>
    public static IHttpClientBuilder AddUpstreamCooldown(
        this IHttpClientBuilder builder,
        Func<HttpRequestMessage, string?>? authenticationScope = null,
        bool followRedirects = false)
    {
        builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.TryAddSingleton<IUpstreamCooldown, UpstreamCooldown>();
        return builder.ConfigureAdditionalHttpMessageHandlers((handlers, services) =>
            handlers.Add(new UpstreamCooldownHandler(
                services.GetRequiredService<IUpstreamCooldown>(),
                authenticationScope,
                followRedirects)));
    }
}
