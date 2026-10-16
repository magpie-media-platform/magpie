using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Sdk.Http;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Moq;
using Moq.Protected;

using NUnit.Framework;

namespace Magpie.Test.Core.Http;

[TestFixture]
internal sealed class UpstreamCooldownTests
{
    private FakeTimeProvider _time = null!;
    private Mock<HttpMessageHandler> _transport = null!;
    private ServiceProvider _services = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));
        _transport = new Mock<HttpMessageHandler>();
        _services = new ServiceCollection()
            .AddSingleton(new HttpOptions { Timeout = TimeSpan.FromSeconds(30) })
            .AddSingleton<TimeProvider>(_time)
            .AddHttpClient("upstream-test")
            .AddUpstreamCooldown(
                request => request.Headers.Authorization?.Parameter,
                followRedirects: true)
            .ConfigurePrimaryHttpMessageHandler(() => _transport.Object)
            .Services.BuildServiceProvider();
        _client = _services.GetRequiredService<IHttpClientFactory>().CreateClient("upstream-test");
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _services.Dispose();
    }

    [Test]
    public void Timeout_IsSixtySeconds_WhenNotConfigured()
    {
        // Arrange
        var options = new HttpOptions();

        // Act
        var timeout = options.Timeout;

        // Assert
        Assert.That(timeout, Is.EqualTo(TimeSpan.FromSeconds(60)));
    }

    [TestCase(HttpStatusCode.TooManyRequests, false)]
    [TestCase(HttpStatusCode.TooManyRequests, true)]
    [TestCase(HttpStatusCode.ServiceUnavailable, false)]
    public async Task SendAsync_ReturnsSuccessAfterWaiting_WhenRetryAfterFitsRequestTimeout(
        HttpStatusCode status,
        bool absoluteDate)
    {
        // Arrange
        using var limited = new HttpResponseMessage(status);
        limited.Headers.RetryAfter = absoluteDate
            ? new RetryConditionHeaderValue(new DateTimeOffset(2026, 10, 11, 12, 0, 20, TimeSpan.Zero))
            : new RetryConditionHeaderValue(TimeSpan.FromSeconds(20));
        _transport.Protected().SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(limited)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ready") });

        // Act
        var pending = _client.GetAsync("https://provider.test/item");
        _time.Advance(TimeSpan.FromSeconds(10));

        // Assert
        Assert.That(pending.IsCompleted, Is.False);
        _transport.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
        _time.Advance(TimeSpan.FromSeconds(10));
        using var result = await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        using var assertions = Assert.EnterMultipleScope();
        Assert.That(await result.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("ready"));
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public void SendAsync_ReturnsOrdinaryTimeout_WhenCooldownExceedsThirtySeconds()
    {
        // Arrange
        using var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromDays(1));
        _transport.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(limited);

        // Act
        var pending = _client.GetAsync("https://provider.test/item");
        _time.Advance(TimeSpan.FromSeconds(29));

        // Assert
        Assert.That(pending.IsCompleted, Is.False);
        _time.Advance(TimeSpan.FromSeconds(1));
        var exception = Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
        Assert.That(exception!.InnerException, Is.TypeOf<TimeoutException>());
        _time.Advance(TimeSpan.FromDays(1));
        _transport.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task SendAsync_PreservesCooldownAfterTimeout_WhenRetryAfterIsAbsent()
    {
        // Arrange
        _transport.Protected().SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.TooManyRequests))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var pending = _client.GetAsync("https://provider.test/item");
        _time.Advance(TimeSpan.FromSeconds(29));

        // Assert
        Assert.That(pending.IsCompleted, Is.False);
        _transport.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
        using var result = await _client.GetAsync("https://provider.test/item").ConfigureAwait(false);
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SendAsync_DoesNotBlockOtherOriginsOrAccounts_WhenOneScopeWaits()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var attempts = new List<string>();
        _transport.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns((HttpRequestMessage request, CancellationToken _) =>
            {
                attempts.Add(request.RequestUri!.Host + ":" + request.Headers.Authorization?.Parameter);
                var response = new HttpResponseMessage(request.RequestUri.Host == "provider.test" &&
                    request.Headers.Authorization?.Parameter == "account-a"
                    ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
                return Task.FromResult(response);
            });
        using var first = new HttpRequestMessage(HttpMethod.Get, "https://provider.test/first");
        first.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "account-a");
        using var second = new HttpRequestMessage(HttpMethod.Get, "https://provider.test/second");
        second.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "account-a");
        using var otherAccount = new HttpRequestMessage(HttpMethod.Get, "https://provider.test/other");
        otherAccount.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "account-b");

        // Act
        var firstPending = _client.SendAsync(first, cancellation.Token);
        var secondPending = _client.SendAsync(second, cancellation.Token);
        using var accountResponse = await _client.SendAsync(otherAccount).ConfigureAwait(false);
        using var originResponse = await _client.GetAsync("https://other.test/item").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstPending.IsCompleted, Is.False);
            Assert.That(secondPending.IsCompleted, Is.False);
            Assert.That(accountResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(originResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(
                attempts,
                Is.EqualTo(["provider.test:account-a", "provider.test:account-b", "other.test:"]));
        }

        await cancellation.CancelAsync().ConfigureAwait(false);
        Assert.CatchAsync<OperationCanceledException>(async () => await firstPending.ConfigureAwait(false));
        Assert.CatchAsync<OperationCanceledException>(async () => await secondPending.ConfigureAwait(false));
        _time.Advance(TimeSpan.FromHours(1));
        Assert.That(attempts, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task SendAsync_ReplaysContentAndDisposesThrottleResponse_WhenPostIsRetried()
    {
        // Arrange
        var bodies = new List<string>();
        using var discardedContent = new StringContent("limited");
        _transport.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken token) =>
            {
                bodies.Add(await request.Content!.ReadAsStringAsync(token).ConfigureAwait(false));
                var response = new HttpResponseMessage(bodies.Count == 1
                    ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
                if (bodies.Count == 1)
                {
                    response.Content = discardedContent;
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(20));
                }

                return response;
            });
        using var content = new StringContent("query=example");

        // Act
        var pending = _client.PostAsync("https://provider.test/search", content);

        // Assert
        Assert.That(pending.IsCompleted, Is.False);
        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await discardedContent.ReadAsStringAsync().ConfigureAwait(false));
        _time.Advance(TimeSpan.FromSeconds(20));
        using var result = await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        using var assertions = Assert.EnterMultipleScope();
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(bodies, Is.EqualTo(["query=example", "query=example"]));
    }

    [Test]
    public void SendAsync_CancelsTheTransport_WhenAnActualAttemptTimesOut()
    {
        // Arrange
        var response = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _transport.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns((HttpRequestMessage _, CancellationToken token) => response.Task.WaitAsync(token));

        // Act
        var pending = _client.GetAsync("https://provider.test/item");
        _time.Advance(TimeSpan.FromSeconds(30));

        // Assert
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
        _transport.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [TestCase("00:00:05", 4)]
    [TestCase("00:01:00", 59)]
    [TestCase("00:02:00", 119)]
    public void SendAsync_UsesConfiguredDeadline_WhenGlobalTimeoutChanges(string configuredTimeout, int beforeDeadline)
    {
        // Arrange
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Http:Timeout"] = configuredTimeout
        }).Build();
        using var services = new ServiceCollection()
            .AddSingleton(configuration.GetSection(HttpOptions.SectionName).Get<HttpOptions>()!)
            .AddSingleton<TimeProvider>(_time)
            .AddHttpClient("configured-timeout")
            .AddUpstreamCooldown()
            .ConfigurePrimaryHttpMessageHandler(() => _transport.Object)
            .Services.BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("configured-timeout");
        _transport.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromDays(1)) }
            });

        // Act
        var pending = client.GetAsync("https://provider.test/item");
        _time.Advance(TimeSpan.FromSeconds(beforeDeadline));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(client.Timeout, Is.EqualTo(TimeSpan.Parse(configuredTimeout, CultureInfo.InvariantCulture)));
            Assert.That(pending.IsCompleted, Is.False);
        }

        _time.Advance(TimeSpan.FromSeconds(1));
        var exception = Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
        Assert.That(exception!.InnerException, Is.TypeOf<TimeoutException>());
    }

    [TestCase(HttpStatusCode.TooManyRequests, 15, 4)]
    [TestCase(HttpStatusCode.Redirect, 0, 19)]
    public async Task SendAsync_KeepsOriginalDeadline_WhenRequestRetriesOrRedirects(
        HttpStatusCode status,
        int waitSeconds,
        int beforeDeadline)
    {
        // Arrange
        var attempts = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _transport.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns((HttpRequestMessage _, CancellationToken token) =>
            {
                if (++attempts == 1)
                {
                    _time.Advance(TimeSpan.FromSeconds(10));
                    return Task.FromResult(new HttpResponseMessage(status)
                    {
                        Headers =
                        {
                            RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(15)),
                            Location = new Uri("https://redirect.test/item")
                        }
                    });
                }

                entered.SetResult();
                return response.Task.WaitAsync(token);
            });

        // Act
        var pending = _client.GetAsync("https://provider.test/item");
        _time.Advance(TimeSpan.FromSeconds(waitSeconds));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        _time.Advance(TimeSpan.FromSeconds(beforeDeadline));

        // Assert
        Assert.That(pending.IsCompleted, Is.False);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
        Assert.That(attempts, Is.EqualTo(2));
    }

    [TestCase("WebhookNotificationProvider", 5)]
    [TestCase("ExtensionService", 60)]
    public async Task SendAsync_AcceptsGlobalTimeout_WhenClientUsesStandardResilience(string clientName, int seconds)
    {
        // Arrange
        var registrations = new ServiceCollection().AddSingleton(new HttpOptions
        {
            Timeout = TimeSpan.FromSeconds(seconds)
        });
        registrations.AddMagpieCore(hosted: false, jobRunner: false);
        registrations.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => _transport.Object);
        using var services = registrations.BuildServiceProvider();
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
        _transport.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        using var response = await client.GetAsync("https://provider.test/item").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReadAsStringAsync_CancelsWithoutBufferingEarly_WhenBodyTimesOutOrCallerCancels(bool callerCancels)
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var body = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new Mock<Stream>();
        stream.SetupGet(value => value.CanRead).Returns(true);
        stream.Setup(value => value.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns((Memory<byte> _, CancellationToken token) => new ValueTask<int>(body.Task.WaitAsync(token)));
        stream.Setup(value => value.ReadAsync(
                It.IsAny<byte[]>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns((byte[] _, int _, int _, CancellationToken token) => body.Task.WaitAsync(token));
        _transport.Protected().SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(20)) }
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream.Object) });

        // Act
        var pending = _client.GetAsync(
            "https://provider.test/item",
            HttpCompletionOption.ResponseHeadersRead);
        _time.Advance(TimeSpan.FromSeconds(20));
        using var result = await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        stream.Verify(
            value => value.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        stream.Verify(value => value.ReadAsync(
                It.IsAny<byte[]>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        var reading = result.Content.ReadAsStringAsync(cancellation.Token);
        if (callerCancels)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        else
        {
            _time.Advance(TimeSpan.FromSeconds(10));
        }

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await reading.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
    }

    [Test]
    public async Task SendAsync_RetriesOnlyRedirectTargetWithoutAuthorization_WhenTargetThrottles()
    {
        // Arrange
        var requests = new List<string>();
        _transport.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns((HttpRequestMessage request, CancellationToken _) =>
            {
                requests.Add(request.RequestUri!.Host + ":" + request.Headers.Authorization?.Parameter);
                if (request.RequestUri.Host == "provider.test")
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                    redirect.Headers.Location = new Uri("https://redirect.test/item");
                    return Task.FromResult(redirect);
                }

                var response = new HttpResponseMessage(requests.Count == 2
                    ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(20));
                return Task.FromResult(response);
            });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://provider.test/item");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "private");

        // Act
        var pending = _client.SendAsync(request);
        _time.Advance(TimeSpan.FromSeconds(20));
        using var result = await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        using var assertions = Assert.EnterMultipleScope();
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(requests, Is.EqualTo(["provider.test:private", "redirect.test:", "redirect.test:"]));
    }

    [Test]
    public async Task SendAsync_ReturnsTheFailure_WhenServiceUnavailableHasNoRetryAfter()
    {
        // Arrange
        _transport.Protected().Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        // Act
        using var result = await _client.GetAsync("https://provider.test/item").ConfigureAwait(false);

        // Assert
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        _transport.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }
}
