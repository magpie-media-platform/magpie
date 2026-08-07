using System;

using Magpie.Extensions.Music;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Music;

[TestFixture]
public class MusicExtensionTests
{
    private static readonly Guid ExpectedExtensionId = new("37c92dfb-37bc-4de4-8e68-e7f9615b378f");

    [Test]
    public void Extension_ReturnsStableIdentity_WhenRead()
    {
        // Arrange
        // Act
        var extension = new MusicExtension();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(extension.Id, Is.EqualTo(ExpectedExtensionId));
            Assert.That(extension.Name, Is.EqualTo("Music"));
            Assert.That(extension.Version, Is.EqualTo(new Version(1, 0, 0)));
            Assert.That(extension.TargetABI, Is.EqualTo("[1.0.0,)"));
        }
    }
}
