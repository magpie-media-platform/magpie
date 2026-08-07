using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class SettingsResolverTests
{
    [TestCase("de-DE", "de")]
    [TestCase("EN", "en")]
    [TestCase("  fr  ", "fr")]
    [TestCase("x", "en")]
    [TestCase("12", "en")]
    [TestCase(null, "en")]
    public async Task ResolveAsync_NormalizesPreferredLanguage_WhenGroupDeclaresNoLanguage(
        string? preference,
        string expectedLanguage)
    {
        // Arrange
        var settings = new Mock<ISettingsService>();
        settings.Setup(value => value.GetExtensionGroupAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingsGroupDetail?)null);
        var subject = new SettingsResolver(
            settings.Object,
            NullLogger<SettingsResolver>.Instance);

        // Act
        var result = await subject.ResolveAsync(preference, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Language, Is.EqualTo(expectedLanguage));
    }

    [Test]
    public async Task ResolveAsync_PrefersConfiguredLanguage_WhenGroupDeclaresOne()
    {
        // Arrange
        var group = new SettingsGroup { Name = "magpie.wikidata" };
        group.Settings.Add(new Setting { Key = "language", Value = "pt-BR" });
        var settings = new Mock<ISettingsService>();
        settings.Setup(value => value.GetExtensionGroupAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SettingsGroupDetail { Group = group });
        var subject = new SettingsResolver(
            settings.Object,
            NullLogger<SettingsResolver>.Instance);

        // Act
        var result = await subject.ResolveAsync("de-DE", default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Language, Is.EqualTo("pt"));
            Assert.That(result.FallbackLanguages, Is.EqualTo(["pt", "en"]));
        }
    }
}
