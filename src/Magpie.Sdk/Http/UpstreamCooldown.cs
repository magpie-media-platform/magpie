using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Sdk.Http;

internal sealed class UpstreamCooldown(TimeProvider timeProvider, HttpOptions options) : IUpstreamCooldown
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Cooldown> _cooldowns = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        string? authenticationScope,
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        var key = Key(request, authenticationScope);
        var remainingWait = options.Timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Cooldown? recovery = null;
            Task? recovering = null;
            var delay = TimeSpan.Zero;
            lock (_sync)
            {
                var now = timeProvider.GetUtcNow();
                if (_cooldowns.TryGetValue(key, out var cooldown))
                {
                    if (cooldown.Recovering)
                    {
                        recovering = cooldown.Completed.Task;
                    }
                    else if (cooldown.RetryAt > now)
                    {
                        delay = cooldown.RetryAt - now;
                    }
                    else if (cooldown.RecoveryUntil != default && cooldown.RecoveryUntil <= now)
                    {
                        _cooldowns.Remove(key);
                    }
                    else
                    {
                        cooldown.Recovering = true;
                        cooldown.Completed = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        if (cooldown.RecoveryUntil == default)
                        {
                            cooldown.RecoveryUntil = now.AddSeconds(30);
                        }

                        recovery = cooldown;
                    }
                }
            }

            if (recovering is not null || delay > TimeSpan.Zero)
            {
                var started = timeProvider.GetTimestamp();
                using var timeout = timeProvider.CreateCancellationTokenSource(remainingWait);
                using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                try
                {
                    if (recovering is not null)
                    {
                        await recovering.WaitAsync(Timeout.InfiniteTimeSpan, timeProvider, waiting.Token)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        if (delay > remainingWait)
                        {
                            delay = remainingWait;
                        }

                        await TimeProviderTaskExtensions.Delay(timeProvider, delay, waiting.Token)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (
                    timeout.IsCancellationRequested &&
                    !cancellationToken.IsCancellationRequested)
                {
                    throw new TaskCanceledException("The HTTP request timed out.", new TimeoutException());
                }

                remainingWait -= timeProvider.GetElapsedTime(started);
                cancellationToken.ThrowIfCancellationRequested();
                if (remainingWait <= TimeSpan.Zero)
                {
                    throw new TaskCanceledException("The HTTP request timed out.", new TimeoutException());
                }

                continue;
            }

            try
            {
                var response = await send(cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                    response.StatusCode == HttpStatusCode.ServiceUnavailable &&
                    response.Headers.Contains("Retry-After"))
                {
                    RecordThrottle(key, response);
                    response.Dispose();
                    continue;
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

                        recovery.Completed.TrySetResult(true);
                    }
                }
            }
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

    private void RecordThrottle(string key, HttpResponseMessage response)
    {
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
        }
    }

    private sealed record Cooldown
    {
        public DateTimeOffset RetryAt { get; set; }

        public DateTimeOffset RecoveryUntil { get; set; }

        public int Throttles { get; set; }

        public bool Recovering { get; set; }

        public TaskCompletionSource<bool> Completed { get; set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
