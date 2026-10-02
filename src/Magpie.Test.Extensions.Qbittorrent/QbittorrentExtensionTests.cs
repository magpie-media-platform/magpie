using System;

using Magpie.Extensions.Qbittorrent;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Qbittorrent;

[TestFixture]
public class QbittorrentExtensionTests
{
    private static readonly Guid ExpectedExtensionId = new("F9238BE4-1471-4C6E-859F-D26D472577EC");

    [Test]
    public void QbittorrentExtension_Identity_WhenCreated()
    {
        // Arrange

        // Act
        var extension = new QbittorrentExtension();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(extension.Id, Is.EqualTo(ExpectedExtensionId));
            Assert.That(extension.Name, Is.EqualTo("qBittorrent"));
        }
    }
}
