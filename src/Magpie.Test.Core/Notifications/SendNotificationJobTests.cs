using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Notifications;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Notifications;

[TestFixture]
public sealed class SendNotificationJobTests
{
    private const string NodeId = "node-1";

    private ILoggerFactory _loggerFactory = null!;
    private Mock<INotificationRuleRepository> _ruleRepository = null!;
    private Mock<INotificationProvider> _webhookProvider = null!;
    private List<(NotificationRule Rule, MagpieEvent MagpieEvent)> _sentNotifications = null!;
    private Exception? _providerFailure;
    private INotificationProvider[] _providers = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerFactory = NullLoggerFactory.Instance;
        _ruleRepository = new Mock<INotificationRuleRepository>();

        _sentNotifications = [];
        _providerFailure = null;
        _webhookProvider = new Mock<INotificationProvider>();
        _webhookProvider.SetupGet(p => p.ProviderType).Returns("webhook");
        _webhookProvider.SetupGet(p => p.SettingsSchema).Returns([]);
        _webhookProvider
            .Setup(p => p.SendAsync(
                It.IsAny<NotificationRule>(),
                It.IsAny<MagpieEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<NotificationRule, MagpieEvent, CancellationToken>((rule, magpieEvent, _) =>
                _sentNotifications.Add((rule, magpieEvent)))
            .Returns<NotificationRule, MagpieEvent, CancellationToken>((_, _, _) =>
                _providerFailure is null ? Task.CompletedTask : Task.FromException(_providerFailure));
        _providers = [_webhookProvider.Object];
    }

    [Test]
    public void Spec_ReturnsBoundedRetryPolicy_WhenRead()
    {
        // Act
        var spec = SendNotificationJob.Spec;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(spec.FailurePolicy, Is.EqualTo(JobFailurePolicy.Retry));
            Assert.That(spec.JobType, Is.EqualTo("SendNotification"));
        }
    }

    [Test]
    public async Task ExecuteAsync_CallsProviderSendAsync_WhenParametersContainAnEvent()
    {
        // Arrange
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "Rule-webhook",
            Enabled = true,
            ProviderType = "webhook",
            Settings = new Dictionary<string, string>
            {
                ["url"] = "https://example.test/hook"
            }
        };
        _ruleRepository
            .Setup(r => r.GetByIdAsync(rule.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rule);

        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>
            {
                ["downloadId"] = "abc",
                ["title"] = "Example"
            },
            now,
            now,
            NodeId);
        var job = new SendNotificationJob(
            _ruleRepository.Object,
            _providers,
            _loggerFactory.CreateLogger<SendNotificationJob>());
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["ruleId"] = rule.Id.ToString("D"),
                ["event"] = JsonSerializer.Serialize(
                    magpieEvent,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
            },
            Event: null);

        // Act
        await job.ExecuteAsync(context, default).ConfigureAwait(false);

        // Assert
        Assert.That(_sentNotifications, Has.Count.EqualTo(1));
        var sent = _sentNotifications[0];
        Assert.That(sent.Rule.Id, Is.EqualTo(rule.Id));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent.MagpieEvent.Id, Is.EqualTo(magpieEvent.Id));
            Assert.That(sent.MagpieEvent.Name, Is.EqualTo("download.completed"));
            Assert.That(sent.MagpieEvent.NodeId, Is.EqualTo(NodeId));
            Assert.That(sent.MagpieEvent.OccurredAt, Is.EqualTo(magpieEvent.OccurredAt));
            Assert.That(sent.MagpieEvent.PublishedAt, Is.EqualTo(magpieEvent.PublishedAt));
            Assert.That(sent.MagpieEvent.Context["downloadId"], Is.EqualTo("abc"));
            Assert.That(sent.MagpieEvent.Context["title"], Is.EqualTo("Example"));
        }
    }

    [Test]
    public void ExecuteAsync_PropagatesFailure_WhenTheProviderThrows()
    {
        // Arrange
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "Rule-webhook",
            Enabled = true,
            ProviderType = "webhook",
            Settings = new Dictionary<string, string>
            {
                ["url"] = "https://example.test/hook"
            }
        };
        _ruleRepository
            .Setup(r => r.GetByIdAsync(rule.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rule);
        _providerFailure = new InvalidOperationException("provider configured to throw");

        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>(),
            now,
            now,
            NodeId);
        var job = new SendNotificationJob(
            _ruleRepository.Object,
            _providers,
            _loggerFactory.CreateLogger<SendNotificationJob>());
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["ruleId"] = rule.Id.ToString("D"),
                ["event"] = JsonSerializer.Serialize(
                    magpieEvent,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
            },
            Event: null);

        // Act
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await job.ExecuteAsync(context, default).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex!.Message, Does.Contain("provider configured to throw"));
            Assert.That(_sentNotifications, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task ExecuteAsync_TerminalNoOp_WhenRuleDeleted()
    {
        // Arrange
        var ruleId = Guid.NewGuid();
        _ruleRepository
            .Setup(r => r.GetByIdAsync(ruleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationRule?)null);

        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>(),
            now,
            now,
            NodeId);
        var job = new SendNotificationJob(
            _ruleRepository.Object,
            _providers,
            _loggerFactory.CreateLogger<SendNotificationJob>());
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["ruleId"] = ruleId.ToString("D"),
                ["event"] = JsonSerializer.Serialize(
                    magpieEvent,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
            },
            Event: null);

        // Act
        await job.ExecuteAsync(context, default).ConfigureAwait(false);

        // Assert
        Assert.That(_sentNotifications, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_TerminalNoOp_WhenProviderUnregistered()
    {
        // Arrange
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid(),
            Name = "Rule-not-registered",
            Enabled = true,
            ProviderType = "not-registered",
            Settings = new Dictionary<string, string>
            {
                ["url"] = "https://example.test/hook"
            }
        };
        _ruleRepository
            .Setup(r => r.GetByIdAsync(rule.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rule);

        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "download.completed",
            new Dictionary<string, string>(),
            now,
            now,
            NodeId);
        var job = new SendNotificationJob(
            _ruleRepository.Object,
            _providers,
            _loggerFactory.CreateLogger<SendNotificationJob>());
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["ruleId"] = rule.Id.ToString("D"),
                ["event"] = JsonSerializer.Serialize(
                    magpieEvent,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
            },
            Event: null);

        // Act
        await job.ExecuteAsync(context, default).ConfigureAwait(false);

        // Assert
        Assert.That(_sentNotifications, Is.Empty);
    }

    [Test]
    public void ExecuteAsync_Throws_WhenRuleIdParameterMissing()
    {
        // Arrange
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(Guid.NewGuid(), "x", new Dictionary<string, string>(), now, now, NodeId);
        var job = new SendNotificationJob(
            _ruleRepository.Object,
            _providers,
            _loggerFactory.CreateLogger<SendNotificationJob>());
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["event"] = JsonSerializer.Serialize(
                    magpieEvent,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
            },
            Event: null);

        // Act
        var ex = Assert.ThrowsAsync<ArgumentException>(
            async () => await job.ExecuteAsync(context, default).ConfigureAwait(false));

        // Assert
        Assert.That(ex!.Message, Does.Contain("A notification rule ID is required."));
    }

    [Test]
    public void ExecuteAsync_Throws_WhenEventParameterMissing()
    {
        // Arrange
        var job = new SendNotificationJob(
            _ruleRepository.Object,
            _providers,
            _loggerFactory.CreateLogger<SendNotificationJob>());
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["ruleId"] = Guid.NewGuid().ToString("D")
            },
            Event: null);

        // Act
        var ex = Assert.ThrowsAsync<ArgumentException>(
            async () => await job.ExecuteAsync(context, default).ConfigureAwait(false));

        // Assert
        Assert.That(ex!.Message, Does.Contain("Event data is required."));
    }

    [Test]
    public void SerializeEvent_PreservesEventFields_WhenStoredInJobParameters()
    {
        // Arrange
        var now = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);
        var magpieEvent = new MagpieEvent(
            Guid.NewGuid(),
            "content.added",
            new Dictionary<string, string>
            {
                ["a"] = "1",
                ["b"] = "two"
            },
            now,
            now,
            NodeId);

        // Act
        var serialized = JsonSerializer.Serialize(magpieEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var deserialized = JsonSerializer.Deserialize<MagpieEvent>(
            serialized,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Assert
        Assert.That(deserialized, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deserialized!.Id, Is.EqualTo(magpieEvent.Id));
            Assert.That(deserialized.Name, Is.EqualTo(magpieEvent.Name));
            Assert.That(deserialized.NodeId, Is.EqualTo(magpieEvent.NodeId));
            Assert.That(deserialized.Context, Is.EquivalentTo(magpieEvent.Context));
        }
    }
}
