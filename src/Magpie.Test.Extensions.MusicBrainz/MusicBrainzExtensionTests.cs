using System;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz;

using NUnit.Framework;

namespace Magpie.Test.Extensions.MusicBrainz;

[TestFixture]
public class MusicBrainzExtensionTests
{
    [Test]
    public void Identity_IsStable_WhenExtensionIsConstructed()
    {
        // Arrange
        var extension = new MusicBrainzExtension();

        // Act
        var id = extension.Id;
        var name = extension.Name;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(id, Is.EqualTo(new Guid("95171BBE-D8F6-4771-9434-860AC2B5BC30")));
            Assert.That(name, Is.EqualTo("MusicBrainz"));
        }
    }

    [TestCase("metadata-settings.js")]
    [TestCase("metadata-settings.html")]
    [TestCase("musicbrainz-settings.js")]
    public async Task GetAssetStreamAsync_ReturnsTheEmbeddedAsset_WhenThePathIsDeclared(string path)
    {
        // Arrange
        var provider = new AdminUiProvider();

        // Act
        using var stream = await provider.GetAssetStreamAsync(path).ConfigureAwait(false);

        // Assert
        Assert.That(stream, Is.Not.Null);
    }
}
