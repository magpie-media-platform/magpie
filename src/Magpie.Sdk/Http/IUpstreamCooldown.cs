using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Sdk.Http;

/// <summary>
/// Shares upstream cooldowns across requests with the same origin and authentication scope.
/// </summary>
public interface IUpstreamCooldown
{
    /// <summary>
    /// Rejects known throttled work before it waits for a local resource.
    /// </summary>
    /// <param name="request">The request with an absolute target URI</param>
    /// <param name="authenticationScope">The stable authentication identity, or null for anonymous access</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    void Check(HttpRequestMessage request, string? authenticationScope, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a request when its upstream permits work.
    /// </summary>
    /// <param name="request">The request with an absolute target URI</param>
    /// <param name="authenticationScope">The stable authentication identity, or null for anonymous access</param>
    /// <param name="send">The transport operation</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        string? authenticationScope,
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a throttle reported in an upstream response body.
    /// </summary>
    /// <param name="request">The request with an absolute target URI</param>
    /// <param name="authenticationScope">The stable authentication identity, or null for anonymous access</param>
    /// <param name="response">The response that contains the retry time</param>
    UpstreamThrottledException RecordThrottle(
        HttpRequestMessage request,
        string? authenticationScope,
        HttpResponseMessage response);
}
