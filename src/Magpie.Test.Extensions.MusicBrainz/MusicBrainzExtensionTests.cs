using System;
using System.IO;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz;

using NUnit.Framework;

namespace Magpie.Test.Extensions.MusicBrainz;

[TestFixture]
public class MusicBrainzExtensionTests
{
    [Test]
    public void MusicBrainzExtension_Identity_WhenCreated()
    {
        // Arrange
        var extension = new MusicBrainzExtension();

        // Act
        var id = extension.Id;
        var name = extension.Name;
        var version = extension.Version;
        var targetAbi = extension.TargetABI;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(id, Is.EqualTo(new Guid("95171BBE-D8F6-4771-9434-860AC2B5BC30")));
            Assert.That(name, Is.EqualTo("MusicBrainz"));
            Assert.That(version, Is.EqualTo(new Version(1, 0, 0)));
            Assert.That(targetAbi, Is.EqualTo("[1.0.0,)"));
        }
    }

    [Test]
    public async Task GetAssetStreamAsync_SettingsAsset_WhenMusicBrainzSettingsAreRequested()
    {
        // Arrange
        var provider = new AdminUiProvider();

        // Act
        using var stream = await provider.GetAssetStreamAsync("musicbrainz-settings.js").ConfigureAwait(false);
        using var reader = new StreamReader(stream!);
        var script = await reader.ReadToEndAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                script,
                Does.Contain(
                    "Cover Art Archive images are licensed per upload and remain copyrighted by " +
                    "their respective owners."));
            Assert.That(script, Does.Contain("one Magpie process per egress IP"));
        }
    }

    [Test]
    public async Task GetAssetStreamAsync_MetadataAssets_WhenMetadataSettingsAreRequested()
    {
        // Arrange
        var provider = new AdminUiProvider();

        // Act
        using var runtimeStream = await provider.GetAssetStreamAsync("metadata-settings.js").ConfigureAwait(false);
        using var templateStream = await provider.GetAssetStreamAsync("metadata-settings.html")
            .ConfigureAwait(false);
        using var runtimeReader = new StreamReader(runtimeStream!);
        var script = await runtimeReader.ReadToEndAsync().ConfigureAwait(false);
        using var templateReader = new StreamReader(templateStream!);
        var template = await templateReader.ReadToEndAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(runtimeStream, Is.Not.Null);
            Assert.That(templateStream, Is.Not.Null);
            Assert.That(script, Does.Contain("new URL(\"metadata-settings.html\", runtimeAssetUrl)"));
            Assert.That(script, Does.Contain("template.content.cloneNode(true)"));
            Assert.That(script, Does.Contain("Admin role required to save settings."));
            Assert.That(script, Does.Not.Contain("innerHTML"));
            Assert.That(script, Does.Not.Contain("<style"));
            Assert.That(script, Does.Not.Contain("<section"));
            Assert.That(template, Does.Contain("<template id=\"metadata-settings-template\">"));
            Assert.That(template, Does.Contain(".description, .field, [data-status], .loading"));
            Assert.That(template, Does.Contain("<form class=\"surface\">"));
        }
    }
}
