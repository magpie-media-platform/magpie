using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tmdb;

[TestFixture]
public class SettingsResolverTests
{
    [Test]
    [NonParallelizable]
    public async Task AcquireContextAsync_ReturnsNull_WhenTheApiKeyIsMissing()
    {
        // Arrange
        var original = Environment.GetEnvironmentVariable(SettingsResolver.EnvApiKeyVariable);
        Environment.SetEnvironmentVariable(SettingsResolver.EnvApiKeyVariable, null);
        try
        {
            var settings = new Mock<ISettingsService>();
            settings.Setup(service => service.GetExtensionGroupAsync(
                new Guid("FE120126-EFD7-4DB1-A3A1-8B8A881E013A"),
                Settings.GroupName,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync((SettingsGroupDetail?)null);
            using var resolver = new SettingsResolver(settings.Object, NullLogger<SettingsResolver>.Instance);

            // Act
            var context = await resolver.AcquireContextAsync("en-US", default).ConfigureAwait(false);

            // Assert
            Assert.That(context, Is.Null);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SettingsResolver.EnvApiKeyVariable, original);
        }
    }

    [Test]
    [NonParallelizable]
    public async Task AcquireContextAsync_UsesTheEnvironmentValues_WhenTheSettingsGroupIsMissing()
    {
        // Arrange
        var originalKey = Environment.GetEnvironmentVariable(SettingsResolver.EnvApiKeyVariable);
        var originalLanguage = Environment.GetEnvironmentVariable(SettingsResolver.EnvLanguageVariable);
        Environment.SetEnvironmentVariable(SettingsResolver.EnvApiKeyVariable, "env-key");
        Environment.SetEnvironmentVariable(SettingsResolver.EnvLanguageVariable, "fr-FR");
        try
        {
            var settings = new Mock<ISettingsService>();
            settings.Setup(service => service.GetExtensionGroupAsync(
                new Guid("FE120126-EFD7-4DB1-A3A1-8B8A881E013A"),
                Settings.GroupName,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync((SettingsGroupDetail?)null);
            using var resolver = new SettingsResolver(settings.Object, NullLogger<SettingsResolver>.Instance);

            // Act
            var context = await resolver.AcquireContextAsync("en-US", default).ConfigureAwait(false);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(context!.ApiKey, Is.EqualTo("env-key"));
                Assert.That(context.Language, Is.EqualTo("fr-FR"));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(SettingsResolver.EnvApiKeyVariable, originalKey);
            Environment.SetEnvironmentVariable(SettingsResolver.EnvLanguageVariable, originalLanguage);
        }
    }

    [Test]
    [NonParallelizable]
    public async Task AcquireContextAsync_PrefersThePersistedValues_WhenTheEnvironmentAlsoSuppliesThem()
    {
        // Arrange
        var originalKey = Environment.GetEnvironmentVariable(SettingsResolver.EnvApiKeyVariable);
        Environment.SetEnvironmentVariable(SettingsResolver.EnvApiKeyVariable, "env-key");
        try
        {
            var group = new SettingsGroup
            {
                Name = Settings.GroupName,
                ExtensionId = new Guid("FE120126-EFD7-4DB1-A3A1-8B8A881E013A")
            };
            group.Settings.Add(new Setting { Key = Settings.ApiKeyKey, Value = "stored-key" });
            group.Settings.Add(new Setting { Key = Settings.LanguageKey, Value = "ja-JP" });
            var settings = new Mock<ISettingsService>();
            settings.Setup(service => service.GetExtensionGroupAsync(
                new Guid("FE120126-EFD7-4DB1-A3A1-8B8A881E013A"),
                Settings.GroupName,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SettingsGroupDetail { Group = group });
            using var resolver = new SettingsResolver(settings.Object, NullLogger<SettingsResolver>.Instance);

            // Act
            var first = await resolver.AcquireContextAsync("en-US", default).ConfigureAwait(false);
            var second = await resolver.AcquireContextAsync("de-DE", default).ConfigureAwait(false);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(first!.ApiKey, Is.EqualTo("stored-key"));
                Assert.That(first.Language, Is.EqualTo("ja-JP"));
                Assert.That(second!.Language, Is.EqualTo("ja-JP"));
                settings.Verify(
                    service => service.GetExtensionGroupAsync(
                        new Guid("FE120126-EFD7-4DB1-A3A1-8B8A881E013A"),
                        Settings.GroupName,
                        It.IsAny<CancellationToken>()),
                    Times.Once);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(SettingsResolver.EnvApiKeyVariable, originalKey);
        }
    }

    [Test]
    public async Task AcquireContextAsync_PrefersTheAncestorLanguage_WhenOneIsSupplied()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = Settings.GroupName,
            ExtensionId = new Guid("FE120126-EFD7-4DB1-A3A1-8B8A881E013A")
        };
        group.Settings.Add(new Setting { Key = Settings.ApiKeyKey, Value = "stored-key" });
        group.Settings.Add(new Setting { Key = Settings.LanguageKey, Value = "ja-JP" });
        var settings = new Mock<ISettingsService>();
        settings.Setup(service => service.GetExtensionGroupAsync(
            new Guid("FE120126-EFD7-4DB1-A3A1-8B8A881E013A"),
            Settings.GroupName,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SettingsGroupDetail { Group = group });
        using var resolver = new SettingsResolver(settings.Object, NullLogger<SettingsResolver>.Instance);

        // Act
        var context = await resolver.AcquireContextAsync(["sv-SE"], "en-US", default).ConfigureAwait(false);

        // Assert
        Assert.That(context!.Language, Is.EqualTo("sv-SE"));
    }
}
