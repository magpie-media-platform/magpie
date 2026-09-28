using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Sdk.Http;

internal sealed class UpstreamCooldownHandler(
    IUpstreamCooldown cooldown,
    Func<HttpRequestMessage, string?>? authenticationScope,
    bool followRedirects) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var current = request;
        var redirects = new List<HttpRequestMessage>();
        try
        {
            for (var count = 0; ; count++)
            {
                var response = await cooldown.SendAsync(current, authenticationScope?.Invoke(current),
                    token => base.SendAsync(current, token), cancellationToken).ConfigureAwait(false);
                var location = response.Headers.Location;
                if (!followRedirects || count >= 50 || location is null ||
                    response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                        HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or (HttpStatusCode)308))
                {
                    return response;
                }

                var target = location.IsAbsoluteUri ? location : new Uri(current.RequestUri!, location);
                if (target.Scheme is not ("http" or "https") ||
                    current.RequestUri!.Scheme == "https" && target.Scheme == "http")
                {
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
        finally
        {
            foreach (var redirect in redirects)
            {
                redirect.Dispose();
            }
        }
    }
}
