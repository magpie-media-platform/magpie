using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Notifications;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;
using Magpie.Sdk.Notifications;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Notifications;

[TestFixture]
public sealed class NotificationServiceSettingsValidationTests
{
    private static readonly Guid RuleId = new("0c9d3a61-2f7e-4c55-9d0e-7b3c1f7e9a21");
    private static readonly Guid CallerUserId = new("5d47ef13-839a-4495-86dd-d0b36876185b");

    private Mock<INotificationRuleRepository> _rules = null!;
    private INotificationService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _rules = new Mock<INotificationRuleRepository>();
        _rules
            .Setup(repository => repository.CreateAsync(
                It.IsAny<NotificationRule>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationRule rule, Guid? _, CancellationToken _) => rule);
        _rules
            .Setup(repository => repository.PatchAsync(
                It.IsAny<NotificationRulePatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var provider = new Mock<INotificationProvider>();
        provider.SetupGet(p => p.ProviderType).Returns("webhook");
        provider.SetupGet(p => p.SettingsSchema).Returns(
        [
            new() { Key = "url", Type = DataType.String, DisplayName = "URL", Required = true },
            new() { Key = "method", Type = DataType.String, DisplayName = "HTTP method" }
        ]);
        var settingsService = new Mock<ISettingsService>();
        settingsService
            .Setup(service => service.ValidateAsync(
                It.IsAny<IReadOnlyList<SettingSchemaDefinition>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntegrationSettingsValidationResult { IsValid = true });
        settingsService
            .Setup(service => service.ValidateAsync(
                It.IsAny<IReadOnlyList<SettingSchemaDefinition>>(),
                It.Is<IReadOnlyDictionary<string, string>>(settings =>
                    string.IsNullOrWhiteSpace(settings.GetValueOrDefault("url"))),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntegrationSettingsValidationResult
            {
                IsValid = false,
                Issues = [new("url", "URL is required.")]
            });
        _subject = new NotificationService(
            _rules.Object,
            [provider.Object],
            settingsService.Object,
            NullLogger<NotificationService>.Instance);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void CreateAsync_RejectsTheRule_WhenARequiredProviderSettingIsMissing(bool enabled)
    {
        // Arrange
        var create = new NotificationRuleCreate
        {
            Name = "QA rule",
            ProviderType = "webhook",
            Settings = new Dictionary<string, string> { ["url"] = " ", ["method"] = "POST" },
            Enabled = enabled
        };

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await _subject.CreateAsync(create, CallerUserId).ConfigureAwait(false),
                Throws.TypeOf<MagpieException>().With.Message.Contains("URL"));
            _rules.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<NotificationRule>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task CreateAsync_CreatesTheRule_WhenRequiredProviderSettingsArePresent()
    {
        // Arrange
        var create = new NotificationRuleCreate
        {
            Name = "QA rule",
            ProviderType = "webhook",
            Settings = new Dictionary<string, string> { ["url"] = "https://example.com/hook" }
        };

        // Act
        var created = await _subject.CreateAsync(create, CallerUserId).ConfigureAwait(false);

        // Assert
        Assert.That(created.Settings["url"], Is.EqualTo("https://example.com/hook"));
    }

    [Test]
    public void PatchAsync_RejectsThePatch_WhenItRemovesARequiredProviderSetting()
    {
        // Arrange
        ArrangeStoredRule(new Dictionary<string, string> { ["url"] = "https://example.com/hook" });
        var patch = new NotificationRulePatch
        {
            Settings = new DictionaryPatch<string, string?, string?> { Remove = ["url"] }
        };

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await _subject.PatchAsync(RuleId, patch, CallerUserId, true).ConfigureAwait(false),
                Throws.TypeOf<MagpieException>().With.Message.Contains("URL"));
            _rules.Verify(
                repository => repository.PatchAsync(
                    It.IsAny<NotificationRulePatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public void PatchAsync_RejectsEnablingTheRule_WhenTheStoredRuleLacksARequiredSetting()
    {
        // Arrange
        ArrangeStoredRule([]);
        var patch = new NotificationRulePatch { Enabled = new() { Value = true } };

        // Act
        // Assert
        Assert.That(
            async () => await _subject.PatchAsync(RuleId, patch, CallerUserId, true).ConfigureAwait(false),
            Throws.TypeOf<MagpieException>().With.Message.Contains("URL"));
    }

    [Test]
    public async Task PatchAsync_AppliesThePatch_WhenItSuppliesTheMissingRequiredSetting()
    {
        // Arrange
        ArrangeStoredRule([]);
        var patch = new NotificationRulePatch
        {
            Settings = new DictionaryPatch<string, string?, string?>
            {
                Add = new Dictionary<string, string?> { ["url"] = "https://example.com/hook" }
            }
        };

        // Act
        var patched = await _subject.PatchAsync(RuleId, patch, CallerUserId, true).ConfigureAwait(false);

        // Assert
        Assert.That(patched, Is.True);
    }

    private void ArrangeStoredRule(Dictionary<string, string> settings)
    {
        _rules
            .Setup(repository => repository.GetByIdAsync(RuleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationRule
            {
                Id = RuleId,
                Name = "QA rule",
                ProviderType = "webhook",
                Settings = settings,
                Enabled = false
            });
    }
}
