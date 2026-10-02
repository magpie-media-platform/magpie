using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Sdk.Http;

internal sealed class UpstreamCooldown(TimeProvider timeProvider) : IUpstreamCooldown
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Cooldown> _cooldowns = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Check(HttpRequestMessage request, string? authenticationScope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(request, authenticationScope);
        lock (_sync)
        {
            var now = timeProvider.GetUtcNow();
            if (_cooldowns.TryGetValue(key, out var cooldown) && (cooldown.RetryAt > now || cooldown.Recovering))
            {
                throw new UpstreamThrottledException(cooldown.RetryAt > now ? cooldown.RetryAt : now.AddSeconds(1));
            }
        }
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        string? authenticationScope,
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(request, authenticationScope);
        Cooldown? recovery = null;
        lock (_sync)
        {
            var now = timeProvider.GetUtcNow();
            if (_cooldowns.TryGetValue(key, out var cooldown))
            {
                if (cooldown.RetryAt > now || cooldown.Recovering)
                {
                    throw new UpstreamThrottledException(cooldown.RetryAt > now ? cooldown.RetryAt : now.AddSeconds(1));
                }

                if (cooldown.RecoveryUntil != default && cooldown.RecoveryUntil <= now)
                {
                    _cooldowns.Remove(key);
                }
                else
                {
                    cooldown.Recovering = true;
                    if (cooldown.RecoveryUntil == default)
                    {
                        cooldown.RecoveryUntil = now.AddSeconds(30);
                    }

                    recovery = cooldown;
                }
            }
        }

        try
        {
            var response = await send(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                response.StatusCode == HttpStatusCode.ServiceUnavailable && response.Headers.Contains("Retry-After"))
            {
                var throttle = RecordThrottle(request, authenticationScope, response);
                response.Dispose();
                throw throttle;
            }

            return response;
        }
        finally
        {
            if (recovery is not null)
            {
                lock (_sync)
                {
                    recovery.Recovering = false;
                    var next = timeProvider.GetUtcNow().AddSeconds(1);
                    if (next > recovery.RetryAt)
                    {
                        recovery.RetryAt = next;
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public UpstreamThrottledException RecordThrottle(
        HttpRequestMessage request,
        string? authenticationScope,
        HttpResponseMessage response)
    {
        var key = Key(request, authenticationScope);
        lock (_sync)
        {
            var now = timeProvider.GetUtcNow();
            if (!_cooldowns.TryGetValue(key, out var cooldown))
            {
                cooldown = new Cooldown();
                _cooldowns.Add(key, cooldown);
            }

            cooldown.Throttles = Math.Min(cooldown.Throttles + 1, 5);
            var retry = response.Headers.RetryAfter;
            var retryAt = retry?.Date;
            if (retry?.Delta is { } delta && delta > TimeSpan.Zero)
            {
                retryAt = delta < DateTimeOffset.MaxValue - now ? now + delta : DateTimeOffset.MaxValue;
            }

            if (retryAt is null || retryAt <= now)
            {
                retryAt = now.AddSeconds(Math.Min(300, 30 * Math.Pow(2, cooldown.Throttles - 1)));
            }

            if (retryAt > cooldown.RetryAt)
            {
                cooldown.RetryAt = retryAt.Value;
            }

            cooldown.RecoveryUntil = default;
            return new UpstreamThrottledException(cooldown.RetryAt, requestSent: true);
        }
    }

    private static string Key(HttpRequestMessage request, string? authenticationScope)
    {
        var origin = request.RequestUri?.GetLeftPart(UriPartial.Authority)
            ?? throw new InvalidOperationException("The upstream request requires an absolute URI.");
        using var hash = SHA256.Create();
        return origin.ToUpperInvariant() + "|" +
            Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(authenticationScope ?? string.Empty)));
    }

    private sealed record Cooldown
    {
        public DateTimeOffset RetryAt { get; set; }

        public DateTimeOffset RecoveryUntil { get; set; }

        public int Throttles { get; set; }

        public bool Recovering { get; set; }
    }
}
