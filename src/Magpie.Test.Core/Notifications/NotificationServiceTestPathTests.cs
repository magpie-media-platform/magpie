using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Notifications;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Notifications;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Notifications;

[TestFixture]
public sealed class NotificationServiceTestPathTests
{
    private Mock<INotificationProvider> _provider = null!;
    private Mock<INotificationRuleRepository> _rules = null!;
    private ConcurrentQueue<(NotificationRule Rule, MagpieEvent MagpieEvent)> _sent = null!;
    private bool _providerThrows;
    private INotificationService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _sent = new ConcurrentQueue<(NotificationRule Rule, MagpieEvent MagpieEvent)>();
        _providerThrows = false;
        _rules = new Mock<INotificationRuleRepository>();
        _provider = new Mock<INotificationProvider>();
        _provider.SetupGet(p => p.ProviderType).Returns("webhook");
        _provider.SetupGet(p => p.SettingsSchema).Returns([]);
        _provider
            .Setup(p => p.SendAsync(
                It.IsAny<NotificationRule>(),
                It.IsAny<MagpieEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<NotificationRule, MagpieEvent, CancellationToken>((rule, magpieEvent, _) =>
                _sent.Enqueue((rule, magpieEvent)))
            .Returns<NotificationRule, MagpieEvent, CancellationToken>((_, _, _) =>
                _providerThrows
                    ? Task.FromException(new InvalidOperationException("provider configured to throw"))
                    : Task.CompletedTask);
        _subject = new NotificationService(
            _rules.Object,
            [_provider.Object],
            NullLogger<NotificationService>.Instance);
    }

    [TestCase(true, true, false, true, TestName = "PatchAsync_ReturnsTrue_WhenCallerOwnsRule")]
    [TestCase(true, false, true, true, TestName = "PatchAsync_ReturnsTrue_WhenCallerIsAdministrator")]
    [TestCase(true, null, true, true, TestName = "PatchAsync_ReturnsTrue_WhenGlobalRuleAndCallerIsAdministrator")]
    [TestCase(true, false, false, false, TestName = "PatchAsync_ReturnsFalse_WhenAnotherUserOwnsRule")]
    [TestCase(true, null, false, false, TestName = "PatchAsync_ReturnsFalse_WhenGlobalRuleAndCallerIsNotAdministrator")]
    [TestCase(false, false, false, false, TestName = "PatchAsync_ReturnsFalse_WhenRuleIsAbsent")]
    public async Task PatchAsync_Result_WhenAccessIsEvaluated(
        bool ruleExists,
        bool? ownerIsCaller,
        bool callerIsAdmin,
        bool expected)
    {
        // Arrange
        var ruleId = Guid.NewGuid();
        var callerUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        NotificationRule? rule = ruleExists
            ? new()
            {
                Id = ruleId,
                Name = "original",
                OwnerUserId = ownerIsCaller is null
                    ? null
                    : ownerIsCaller.Value ? callerUserId : otherUserId,
                ProviderType = "webhook",
            }
            : null;
        _rules
            .Setup(repository => repository.GetByIdAsync(ruleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rule);
        _rules
            .Setup(repository => repository.UpdateAsync(
                It.IsAny<NotificationRule>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var patch = new NotificationRulePatch { Name = new() { Value = "patched" } };

        // Act
        var result = await _subject
            .PatchAsync(ruleId, patch, callerUserId, callerIsAdmin)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(rule?.Name, Is.EqualTo(ruleExists ? expected ? "patched" : "original" : null));
        }

        _rules.Verify(
            repository => repository.UpdateAsync(
                It.IsAny<NotificationRule>(),
                callerUserId,
                It.IsAny<CancellationToken>()),
            Times.Exactly(expected ? 1 : 0));
    }

    [TestCase(true, true, false, true, TestName = "DeleteAsync_ReturnsTrue_WhenCallerOwnsRule")]
    [TestCase(true, false, true, true, TestName = "DeleteAsync_ReturnsTrue_WhenCallerIsAdministrator")]
    [TestCase(true, null, true, true, TestName = "DeleteAsync_ReturnsTrue_WhenGlobalRuleAndCallerIsAdministrator")]
    [TestCase(true, false, false, false, TestName = "DeleteAsync_ReturnsFalse_WhenAnotherUserOwnsRule")]
    [TestCase(
        true,
        null,
        false,
        false,
        TestName = "DeleteAsync_ReturnsFalse_WhenGlobalRuleAndCallerIsNotAdministrator")]
    [TestCase(false, false, false, false, TestName = "DeleteAsync_ReturnsFalse_WhenRuleIsAbsent")]
    public async Task DeleteAsync_Result_WhenAccessIsEvaluated(
        bool ruleExists,
        bool? ownerIsCaller,
        bool callerIsAdmin,
        bool expected)
    {
        // Arrange
        var ruleId = Guid.NewGuid();
        var callerUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        NotificationRule? rule = ruleExists
            ? new()
            {
                Id = ruleId,
                Name = "rule",
                OwnerUserId = ownerIsCaller is null
                    ? null
                    : ownerIsCaller.Value ? callerUserId : otherUserId,
                ProviderType = "webhook",
            }
            : null;
        _rules
            .Setup(repository => repository.GetByIdAsync(ruleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rule);
        _rules
            .Setup(repository => repository.DeleteAsync(
                ruleId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject
            .DeleteAsync(ruleId, callerUserId, callerIsAdmin)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
        _rules.Verify(
            repository => repository.DeleteAsync(
                ruleId,
                callerUserId,
                It.IsAny<CancellationToken>()),
            Times.Exactly(expected ? 1 : 0));
    }

    [TestCase(true, true, false, true, TestName = "TestAsync_ReturnsResult_WhenCallerOwnsRule")]
    [TestCase(true, false, true, true, TestName = "TestAsync_ReturnsResult_WhenCallerIsAdministrator")]
    [TestCase(true, null, true, true, TestName = "TestAsync_ReturnsResult_WhenGlobalRuleAndCallerIsAdministrator")]
    [TestCase(true, false, false, false, TestName = "TestAsync_ReturnsNull_WhenAnotherUserOwnsRule")]
    [TestCase(true, null, false, false, TestName = "TestAsync_ReturnsNull_WhenGlobalRuleAndCallerIsNotAdministrator")]
    [TestCase(false, false, false, false, TestName = "TestAsync_ReturnsNull_WhenRuleIsAbsent")]
    public async Task TestAsync_Result_WhenAccessIsEvaluated(
        bool ruleExists,
        bool? ownerIsCaller,
        bool callerIsAdmin,
        bool expected)
    {
        // Arrange
        var ruleId = Guid.NewGuid();
        var callerUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        NotificationRule? rule = ruleExists
            ? new()
            {
                Id = ruleId,
                Name = "rule",
                OwnerUserId = ownerIsCaller is null
                    ? null
                    : ownerIsCaller.Value ? callerUserId : otherUserId,
                ProviderType = "webhook",
            }
            : null;
        _rules
            .Setup(repository => repository.GetByIdAsync(ruleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rule);

        // Act
        var result = await _subject
            .TestAsync(ruleId, callerUserId, callerIsAdmin)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result is not null, Is.EqualTo(expected));
            Assert.That(_sent, Has.Count.EqualTo(expected ? 1 : 0));
        }
    }

    [Test]
    public async Task TestSettingsAsync_SucceedsAndDeliversSyntheticEvent_WhenProviderRegistered()
    {
        // Arrange
        var rule = new NotificationRuleCreate
        {
            Name = "test rule",
            ProviderType = "webhook",
            Settings = new Dictionary<string, string> { ["url"] = "https://example.test/hook" }
        };

        // Act
        var result = await _subject.TestSettingsAsync(rule, default).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(_sent, Has.Count.EqualTo(1));
        }

        var sent = _sent.ToArray()[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent.Rule.Name, Is.EqualTo("test rule"));
            Assert.That(sent.MagpieEvent.Name, Is.EqualTo("magpie.notification.test"));
            Assert.That(sent.MagpieEvent.Context["rule.name"], Is.EqualTo("test rule"));
            Assert.That(sent.MagpieEvent.Context["test"], Is.EqualTo("true"));
        }
    }

    [Test]
    public async Task TestSettingsAsync_Fails_WhenProviderUnregistered()
    {
        // Arrange
        var rule = new NotificationRuleCreate
        {
            Name = "test rule",
            ProviderType = "missing",
            Settings = new Dictionary<string, string> { ["url"] = "https://example.test/hook" }
        };

        // Act
        var result = await _subject.TestSettingsAsync(rule, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("missing"));
            Assert.That(_sent, Is.Empty);
        }
    }

    [Test]
    public async Task TestSettingsAsync_Fails_WhenProviderThrows()
    {
        // Arrange
        var rule = new NotificationRuleCreate
        {
            Name = "test rule",
            ProviderType = "webhook",
            Settings = new Dictionary<string, string> { ["url"] = "https://example.test/hook" }
        };
        _providerThrows = true;

        // Act
        var result = await _subject.TestSettingsAsync(rule, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("provider configured to throw"));
            Assert.That(_sent, Has.Count.EqualTo(1));
        }
    }
}
