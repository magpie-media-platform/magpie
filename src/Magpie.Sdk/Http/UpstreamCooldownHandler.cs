using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Sdk.Http;

internal sealed class UpstreamCooldownHandler(
    IUpstreamCooldown cooldown,
    TimeProvider timeProvider,
    Func<HttpRequestMessage, string?>? authenticationScope,
    bool followRedirects,
    TimeSpan globalTimeout,
    TimeSpan? requestTimeout) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var duration = request.GetTransportTimeout() ?? requestTimeout ?? globalTimeout;
        var timeout = timeProvider.CreateCancellationTokenSource(duration < globalTimeout ? duration : globalTimeout);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var current = request;
        var redirects = new List<HttpRequestMessage>();
        HttpResponseMessage? response = null;
        try
        {
            for (var count = 0; ; count++)
            {
                response = await cooldown.SendAsync(
                    current,
                    authenticationScope?.Invoke(current),
                    token => SendAttemptAsync(current, token),
                    linked.Token).ConfigureAwait(false);
                var location = response.Headers.Location;
                if (!followRedirects || count >= 50 || location is null ||
                    response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                        HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or (HttpStatusCode)308))
                {
                    response.Content = new UpstreamResponseContent(response.Content, timeout, linked, timeProvider);
                    return response;
                }

                var target = location.IsAbsoluteUri ? location : new Uri(current.RequestUri!, location);
                if (target.Scheme is not ("http" or "https") ||
                    current.RequestUri!.Scheme == "https" && target.Scheme == "http")
                {
                    response.Content = new UpstreamResponseContent(response.Content, timeout, linked, timeProvider);
                    return response;
                }

                var get = response.StatusCode == HttpStatusCode.SeeOther && current.Method != HttpMethod.Head ||
                    response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect &&
                    current.Method == HttpMethod.Post;
                var redirect = new HttpRequestMessage(get ? HttpMethod.Get : current.Method, target)
                {
                    Content = get ? null : current.Content
                };
                foreach (var header in current.Headers)
                {
                    if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) &&
                        !header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                    {
                        redirect.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                redirects.Add(redirect);
                response.Dispose();
                current = redirect;
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            response?.Dispose();
            linked.Dispose();
            timeout.Dispose();
            throw new TaskCanceledException("The HTTP request timed out.", new TimeoutException());
        }
        catch
        {
            response?.Dispose();
            linked.Dispose();
            timeout.Dispose();
            throw;
        }
        finally
        {
            foreach (var redirect in redirects)
            {
                redirect.Dispose();
            }
        }
    }

    private async Task<HttpResponseMessage> SendAttemptAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var attempt = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };
        foreach (var header in request.Headers)
        {
            attempt.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        request.CopyOptionsTo(attempt);
#if !NETSTANDARD2_1
        attempt.VersionPolicy = request.VersionPolicy;
#endif
        if (request.Content is not null)
        {
#if NETSTANDARD2_1
            var bytes = await request.Content.ReadAsByteArrayAsync()
                .WaitAsync(Timeout.InfiniteTimeSpan, timeProvider, cancellationToken).ConfigureAwait(false);
#else
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#endif
            attempt.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                attempt.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        var response = await base.SendAsync(attempt, cancellationToken).ConfigureAwait(false);
        response.RequestMessage = request;
        return response;
    }
}
