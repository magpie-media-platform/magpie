using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Sdk.Http;

/// <summary>
/// Shares upstream cooldowns across requests with the same origin and authentication scope.
/// </summary>
public interface IUpstreamCooldown
{
    /// <summary>
    /// Sends a request when its upstream permits work.
    /// </summary>
    /// <param name="request">The request with an absolute target URI</param>
    /// <param name="authenticationScope">The stable authentication identity, or null for anonymous access</param>
    /// <param name="send">The transport operation, which can be called again after a cooldown</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        string? authenticationScope,
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken);
}
