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
    private const string RuleId = "cfd83b0a-97a3-45ae-802a-acd453d15182";
    private const string CallerUserId = "5d47ef13-839a-4495-86dd-d0b36876185b";
    private const string OtherUserId = "342f1f32-14b7-4ab9-85c9-1e9a385b65d5";

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

    [TestCase(CallerUserId, false, TestName = "PatchAsync_AppliesThePatch_WhenTheCallerOwnsTheRule")]
    [TestCase(OtherUserId, true, TestName = "PatchAsync_AppliesThePatch_WhenTheCallerIsAnAdministrator")]
    [TestCase(null, true, TestName = "PatchAsync_AppliesThePatch_WhenTheRuleIsGlobalAndTheCallerIsAnAdministrator")]
    public async Task PatchAsync_AppliesThePatch_WhenTheCallerMayEditTheRule(string? ownerUserId, bool callerIsAdmin)
    {
        // Arrange
        var rule = new NotificationRule
        {
            Id = new Guid(RuleId),
            Name = "original",
            OwnerUserId = ownerUserId is null ? null : new Guid(ownerUserId),
            ProviderType = "webhook",
        };
        _rules
            .Setup(repository => repository.GetByIdAsync(new Guid(RuleId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rule);
        NotificationRulePatch? applied = null;
        Guid? appliedBy = null;
        _rules
            .Setup(repository => repository.PatchAsync(
                It.IsAny<NotificationRulePatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback((NotificationRulePatch rulePatch, Guid? actor, CancellationToken _) =>
            {
                applied = rulePatch;
                appliedBy = actor;
            })
            .ReturnsAsync(true);
        var patch = new NotificationRulePatch { Name = new() { Value = "patched" } };

        // Act
        var result = await _subject
            .PatchAsync(new Guid(RuleId), patch, new Guid(CallerUserId), callerIsAdmin)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(applied!.Id, Is.EqualTo(new Guid(RuleId)));
            Assert.That(applied.Name!.Value, Is.EqualTo("patched"));
            Assert.That(appliedBy, Is.EqualTo(new Guid(CallerUserId)));
            Assert.That(rule.Name, Is.EqualTo("original"));
        }
    }

    [TestCase(OtherUserId, TestName = "PatchAsync_ReturnsFalse_WhenAnotherUserOwnsTheRule")]
    [TestCase(null, TestName = "PatchAsync_ReturnsFalse_WhenTheRuleIsGlobalAndTheCallerIsNotAnAdministrator")]
    public async Task PatchAsync_ReturnsFalse_WhenTheCallerMayNotEditTheRule(string? ownerUserId)
    {
        // Arrange
        var rule = new NotificationRule
        {
            Id = new Guid(RuleId),
            Name = "original",
            OwnerUserId = ownerUserId is null ? null : new Guid(ownerUserId),
            ProviderType = "webhook",
        };
        _rules
            .Setup(repository => repository.GetByIdAsync(new Guid(RuleId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rule);
        NotificationRulePatch? applied = null;
        _rules
            .Setup(repository => repository.PatchAsync(
                It.IsAny<NotificationRulePatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback((NotificationRulePatch rulePatch, Guid? _, CancellationToken _) => applied = rulePatch)
            .ReturnsAsync(true);
        var patch = new NotificationRulePatch { Name = new() { Value = "patched" } };

        // Act
        var result = await _subject
            .PatchAsync(new Guid(RuleId), patch, new Guid(CallerUserId), false)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(applied, Is.Null);
            Assert.That(rule.Name, Is.EqualTo("original"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenTheRuleIsAbsent()
    {
        // Arrange
        _rules
            .Setup(repository => repository.GetByIdAsync(new Guid(RuleId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationRule?)null);
        NotificationRulePatch? applied = null;
        _rules
            .Setup(repository => repository.PatchAsync(
                It.IsAny<NotificationRulePatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback((NotificationRulePatch rulePatch, Guid? _, CancellationToken _) => applied = rulePatch)
            .ReturnsAsync(true);
        var patch = new NotificationRulePatch { Name = new() { Value = "patched" } };

        // Act
        var result = await _subject
            .PatchAsync(new Guid(RuleId), patch, new Guid(CallerUserId), true)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(applied, Is.Null);
        }
    }

    [TestCase(CallerUserId, false, TestName = "DeleteAsync_DeletesTheRule_WhenTheCallerOwnsIt")]
    [TestCase(OtherUserId, true, TestName = "DeleteAsync_DeletesTheRule_WhenTheCallerIsAnAdministrator")]
    [TestCase(null, true, TestName = "DeleteAsync_DeletesTheRule_WhenTheRuleIsGlobalAndTheCallerIsAnAdministrator")]
    public async Task DeleteAsync_DeletesTheRule_WhenTheCallerMayDeleteIt(string? ownerUserId, bool callerIsAdmin)
    {
        // Arrange
        var rule = new NotificationRule
        {
            Id = new Guid(RuleId),
            Name = "rule",
            OwnerUserId = ownerUserId is null ? null : new Guid(ownerUserId),
            ProviderType = "webhook",
        };
        _rules
            .Setup(repository => repository.GetByIdAsync(new Guid(RuleId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rule);
        Guid? deletedBy = null;
        _rules
            .Setup(repository => repository.DeleteAsync(
                new Guid(RuleId),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback((Guid _, Guid? actor, CancellationToken _) => deletedBy = actor)
            .ReturnsAsync(true);

        // Act
        var result = await _subject
            .DeleteAsync(new Guid(RuleId), new Guid(CallerUserId), callerIsAdmin)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(deletedBy, Is.EqualTo(new Guid(CallerUserId)));
        }
    }

    [TestCase(OtherUserId, TestName = "DeleteAsync_ReturnsFalse_WhenAnotherUserOwnsTheRule")]
    [TestCase(null, TestName = "DeleteAsync_ReturnsFalse_WhenTheRuleIsGlobalAndTheCallerIsNotAnAdministrator")]
    public async Task DeleteAsync_ReturnsFalse_WhenTheCallerMayNotDeleteTheRule(string? ownerUserId)
    {
        // Arrange
        var rule = new NotificationRule
        {
            Id = new Guid(RuleId),
            Name = "rule",
            OwnerUserId = ownerUserId is null ? null : new Guid(ownerUserId),
            ProviderType = "webhook",
        };
        _rules
            .Setup(repository => repository.GetByIdAsync(new Guid(RuleId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rule);
        var deleteCalls = 0;
        _rules
            .Setup(repository => repository.DeleteAsync(
                new Guid(RuleId),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => deleteCalls++)
            .ReturnsAsync(true);

        // Act
        var result = await _subject
            .DeleteAsync(new Guid(RuleId), new Guid(CallerUserId), false)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(deleteCalls, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenTheRuleIsAbsent()
    {
        // Arrange
        _rules
            .Setup(repository => repository.GetByIdAsync(new Guid(RuleId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationRule?)null);
        var deleteCalls = 0;
        _rules
            .Setup(repository => repository.DeleteAsync(
                new Guid(RuleId),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => deleteCalls++)
            .ReturnsAsync(true);

        // Act
        var result = await _subject
            .DeleteAsync(new Guid(RuleId), new Guid(CallerUserId), true)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(deleteCalls, Is.EqualTo(0));
        }
    }

    [TestCase(CallerUserId, false, TestName = "TestAsync_SendsTheNotification_WhenTheCallerOwnsTheRule")]
    [TestCase(OtherUserId, true, TestName = "TestAsync_SendsTheNotification_WhenTheCallerIsAnAdministrator")]
    [TestCase(
        null,
        true,
        TestName = "TestAsync_SendsTheNotification_WhenTheRuleIsGlobalAndTheCallerIsAnAdministrator")]
    public async Task TestAsync_SendsTheNotification_WhenTheCallerMayTestTheRule(
        string? ownerUserId,
        bool callerIsAdmin)
    {
        // Arrange
        _rules
            .Setup(repository => repository.GetByIdAsync(new Guid(RuleId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationRule
            {
                Id = new Guid(RuleId),
                Name = "rule",
                OwnerUserId = ownerUserId is null ? null : new Guid(ownerUserId),
                ProviderType = "webhook",
            });

        // Act
        var result = await _subject
            .TestAsync(new Guid(RuleId), new Guid(CallerUserId), callerIsAdmin)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(_sent, Has.Count.EqualTo(1));
        }
    }

    [TestCase(OtherUserId, TestName = "TestAsync_ReturnsNull_WhenAnotherUserOwnsTheRule")]
    [TestCase(null, TestName = "TestAsync_ReturnsNull_WhenTheRuleIsGlobalAndTheCallerIsNotAnAdministrator")]
    public async Task TestAsync_ReturnsNull_WhenTheCallerMayNotTestTheRule(string? ownerUserId)
    {
        // Arrange
        _rules
            .Setup(repository => repository.GetByIdAsync(new Guid(RuleId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationRule
            {
                Id = new Guid(RuleId),
                Name = "rule",
                OwnerUserId = ownerUserId is null ? null : new Guid(ownerUserId),
                ProviderType = "webhook",
            });

        // Act
        var result = await _subject
            .TestAsync(new Guid(RuleId), new Guid(CallerUserId), false)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Null);
            Assert.That(_sent, Is.Empty);
        }
    }

    [Test]
    public async Task TestAsync_ReturnsNull_WhenTheRuleIsAbsent()
    {
        // Arrange
        _rules
            .Setup(repository => repository.GetByIdAsync(new Guid(RuleId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationRule?)null);

        // Act
        var result = await _subject
            .TestAsync(new Guid(RuleId), new Guid(CallerUserId), true)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Null);
            Assert.That(_sent, Is.Empty);
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
