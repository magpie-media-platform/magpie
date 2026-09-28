using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Core.Notifications;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Notifications;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

using RichardSzalay.MockHttp;

namespace Magpie.Test.Core.Notifications;

[TestFixture]
public sealed class WebhookNotificationProviderTests
{
    private const string NodeId = "node-1";

    private MockHttpMessageHandler _http = null!;
    private Mock<IHttpClientFactory> _httpClientFactory = null!;
    private INotificationProvider _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _http = new MockHttpMessageHandler();
        _httpClientFactory = new Mock<IHttpClientFactory>();
        _httpClientFactory
            .Setup(f => f.CreateClient(WebhookNotificationProvider.HttpClientName))
            .Returns(() => _http.ToHttpClient());

        var hostEnvironment = new MagpieHostEnvironment(
            NodeId: NodeId,
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: "/magpie",
            ExtensionsRoot: "/magpie/extensions",
            ConfigRoot: "/magpie/config",
            DataRoot: "/magpie/data",
            StartedAt: DateTime.UtcNow);
        var loggerFactory = NullLoggerFactory.Instance;
        _subject = new WebhookNotificationProvider(
            _httpClientFactory.Object,
            hostEnvironment,
            loggerFactory.CreateLogger<WebhookNotificationProvider>());
    }

    [TearDown]
    public void TearDown()
    {
        ((IDisposable)_subject).Dispose();
        _http.Dispose();
    }

    [Test]
    public void ProviderType_ReturnsWebhook_WhenRead()
    {
        // Act
        // Assert
        Assert.That(_subject.ProviderType, Is.EqualTo("webhook"));
    }

    [Test]
    public void SettingsSchema_ContainsUrlMethodAndHeaders_WhenRead()
    {
        // Act
        var keys = _subject.SettingsSchema.Select(s => s.Key).ToArray();

        // Assert
        Assert.That(keys, Is.EquivalentTo(["url", "method", "headers"]));
    }

    [Test]
    public async Task SendAsync_PostsRule_WhenTheRuleAndEventAreProvided()
    {
        // Arrange
        var request = _http.Expect(HttpMethod.Post, "https://example.test/hook")
            .With(r => r.Content?.Headers.ContentType?.MediaType == "application/json")
            .WithPartialContent("download.completed")
            .WithPartialContent("downloadId")
            .WithPartialContent("abc")
            .WithPartialContent(NodeId)
            .Respond(HttpStatusCode.OK);
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "test",
            ProviderType = "webhook",
            Enabled = true,
            Settings = new Dictionary<string, string>
            {
                ["url"] = "https://example.test/hook"
            }
        };
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>
            {
                ["downloadId"] = "abc"
            },
            now,
            now,
            NodeId);

        // Act
        await _subject.SendAsync(rule, magpieEvent, default).ConfigureAwait(false);

        // Assert
        _http.VerifyNoOutstandingExpectation();
        Assert.That(_http.GetMatchCount(request), Is.EqualTo(1));
    }

    [Test]
    public async Task SendAsync_SendsCustomHeaders_WhenConfigured()
    {
        // Arrange
        var request = _http.Expect(HttpMethod.Post, "https://example.test/hook")
            .WithHeaders("X-Magpie-Sig", "secret")
            .Respond(HttpStatusCode.OK);
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "test",
            ProviderType = "webhook",
            Enabled = true,
            Settings = new Dictionary<string, string>
            {
                ["url"] = "https://example.test/hook",
                ["headers"] = """{"X-Magpie-Sig":"secret"}"""
            }
        };
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>(),
            now,
            now,
            NodeId);

        // Act
        await _subject.SendAsync(rule, magpieEvent, default).ConfigureAwait(false);

        // Assert
        _http.VerifyNoOutstandingExpectation();
        Assert.That(_http.GetMatchCount(request), Is.EqualTo(1));
    }

    [Test]
    public async Task SendAsync_SkipsRule_WhenUrlMissing()
    {
        // Arrange
        var request = _http.Fallback.Respond(HttpStatusCode.OK);
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "test",
            ProviderType = "webhook",
            Enabled = true,
            Settings = []
        };
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>(),
            now,
            now,
            NodeId);

        // Act
        await _subject.SendAsync(rule, magpieEvent, default).ConfigureAwait(false);

        // Assert
        Assert.That(_http.GetMatchCount(request), Is.Zero);
    }

    [Test]
    public void DisplayName_NamesTheProductForPickers_WhenTheProviderOverridesIt()
    {
        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_subject.DisplayName, Is.EqualTo("Webhook"));
            Assert.That(((INotificationProvider)new UnnamedProvider()).DisplayName, Is.EqualTo("unnamed"));
        }
    }

    [TestCase("not-a-uri", false)]
    [TestCase("hooks/relative", false)]
    [TestCase("ftp://example.test/hook", false)]
    [TestCase("https://example.test/hook", true)]
    [TestCase("", true)]
    public async Task ValidateSettingsAsync_AcceptsOnlyAnAbsoluteHttpUrl_WhenAUrlIsSet(string url, bool valid)
    {
        // Act
        var result = await ((IValidatesIntegrationSettings)_subject)
            .ValidateSettingsAsync(new Dictionary<string, string> { ["url"] = url })
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.IsValid, Is.EqualTo(valid));
    }

    [Test]
    public async Task SendAsync_SkipsRule_WhenUrlInvalid()
    {
        // Arrange
        var request = _http.Fallback.Respond(HttpStatusCode.OK);
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "test",
            ProviderType = "webhook",
            Enabled = true,
            Settings = new Dictionary<string, string>
            {
                ["url"] = "not-a-uri"
            }
        };
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>(),
            now,
            now,
            NodeId);

        // Act
        await _subject.SendAsync(rule, magpieEvent, default).ConfigureAwait(false);

        // Assert
        Assert.That(_http.GetMatchCount(request), Is.Zero);
    }

    [Test]
    public void SendAsync_Throws_WhenServerError()
    {
        // Arrange
        var request = _http.Expect(HttpMethod.Post, "https://example.test/fail")
            .Respond(HttpStatusCode.InternalServerError);
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "test",
            ProviderType = "webhook",
            Enabled = true,
            Settings = new Dictionary<string, string>
            {
                ["url"] = "https://example.test/fail"
            }
        };
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>(),
            now,
            now,
            NodeId);

        // Act
        Assert.ThrowsAsync<HttpRequestException>(async () =>
            await _subject.SendAsync(rule, magpieEvent, default).ConfigureAwait(false));

        // Assert
        _http.VerifyNoOutstandingExpectation();
        Assert.That(_http.GetMatchCount(request), Is.EqualTo(1));
    }

    [Test]
    public void SendAsync_Throws_WhenTooManyRequests()
    {
        // Arrange
        var request = _http.Expect(HttpMethod.Post, "https://example.test/throttled")
            .Respond(HttpStatusCode.TooManyRequests);
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "test",
            ProviderType = "webhook",
            Enabled = true,
            Settings = new Dictionary<string, string>
            {
                ["url"] = "https://example.test/throttled"
            }
        };
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>(),
            now,
            now,
            NodeId);

        // Act
        Assert.ThrowsAsync<HttpRequestException>(async () =>
            await _subject.SendAsync(rule, magpieEvent, default).ConfigureAwait(false));

        // Assert
        _http.VerifyNoOutstandingExpectation();
        Assert.That(_http.GetMatchCount(request), Is.EqualTo(1));
    }

    [Test]
    public void SendAsync_Throws_WhenTransportException()
    {
        // Arrange
        var request = _http.Expect(HttpMethod.Post, "https://example.test/down")
            .Throw(new HttpRequestException("connection refused"));
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "test",
            ProviderType = "webhook",
            Enabled = true,
            Settings = new Dictionary<string, string>
            {
                ["url"] = "https://example.test/down"
            }
        };
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>(),
            now,
            now,
            NodeId);

        // Act
        var ex = Assert.ThrowsAsync<HttpRequestException>(async () =>
            await _subject.SendAsync(rule, magpieEvent, default).ConfigureAwait(false));

        // Assert
        _http.VerifyNoOutstandingExpectation();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_http.GetMatchCount(request), Is.EqualTo(1));
            Assert.That(ex!.InnerException, Is.TypeOf<HttpRequestException>());
        }
    }

    [Test]
    public void SendAsync_Throws_WhenTimeout()
    {
        // Arrange
        var request = _http.Expect(HttpMethod.Post, "https://example.test/slow")
            .Throw(new TaskCanceledException("request timed out"));
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "test",
            ProviderType = "webhook",
            Enabled = true,
            Settings = new Dictionary<string, string>
            {
                ["url"] = "https://example.test/slow"
            }
        };
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>(),
            now,
            now,
            NodeId);

        // Act
        Assert.ThrowsAsync<HttpRequestException>(async () =>
            await _subject.SendAsync(rule, magpieEvent, default).ConfigureAwait(false));

        // Assert
        _http.VerifyNoOutstandingExpectation();
        Assert.That(_http.GetMatchCount(request), Is.EqualTo(1));
    }

    [Test]
    public void SendAsync_DoesNotThrow_WhenClientError()
    {
        // Arrange
        var request = _http.Expect(HttpMethod.Post, "https://example.test/bad")
            .Respond(HttpStatusCode.BadRequest);
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "test",
            ProviderType = "webhook",
            Enabled = true,
            Settings = new Dictionary<string, string>
            {
                ["url"] = "https://example.test/bad"
            }
        };
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>(),
            now,
            now,
            NodeId);

        // Act
        Assert.DoesNotThrowAsync(async () =>
            await _subject.SendAsync(rule, magpieEvent, default).ConfigureAwait(false));

        // Assert
        _http.VerifyNoOutstandingExpectation();
        Assert.That(_http.GetMatchCount(request), Is.EqualTo(1));
    }

    [Test]
    public void SendAsync_DoesNotThrow_WhenNotFound()
    {
        // Arrange
        var request = _http.Expect(HttpMethod.Post, "https://example.test/missing")
            .Respond(HttpStatusCode.NotFound);
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "test",
            ProviderType = "webhook",
            Enabled = true,
            Settings = new Dictionary<string, string>
            {
                ["url"] = "https://example.test/missing"
            }
        };
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>(),
            now,
            now,
            NodeId);

        // Act
        Assert.DoesNotThrowAsync(async () =>
            await _subject.SendAsync(rule, magpieEvent, default).ConfigureAwait(false));

        // Assert
        _http.VerifyNoOutstandingExpectation();
        Assert.That(_http.GetMatchCount(request), Is.EqualTo(1));
    }

    [Test]
    public void SendAsync_RethrowsCancellation_WhenCallerCancels()
    {
        // Arrange
        var request = _http.Fallback.Respond(HttpStatusCode.OK);
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "test",
            ProviderType = "webhook",
            Enabled = true,
            Settings = new Dictionary<string, string>
            {
                ["url"] = "https://example.test/cancel"
            }
        };
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>(),
            now,
            now,
            NodeId);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var ex = Assert.CatchAsync(async () =>
            await _subject.SendAsync(rule, magpieEvent, cts.Token).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex, Is.InstanceOf<OperationCanceledException>());
            Assert.That(ex, Is.Not.InstanceOf<HttpRequestException>());
            Assert.That(_http.GetMatchCount(request), Is.Zero);
        }
    }

    private sealed class UnnamedProvider : INotificationProvider
    {
        public string ProviderType => "unnamed";

        public IReadOnlyList<SettingSchemaDefinition> SettingsSchema => [];

        public Task SendAsync(
            NotificationRule rule,
            MagpieEvent magpieEvent,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
