using System;

using Magpie.Extensions.Jellyfin;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Jellyfin;

[TestFixture]
public class JellyfinExtensionTests
{
    private static readonly Guid ExpectedExtensionId = new("6701BA12-86A7-4746-8B35-144B5B167B8D");

    [Test]
    public void Identity_ReturnsStableValue_WhenRead()
    {
        // Arrange

        // Act
        var extension = new JellyfinExtension();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(extension.Id, Is.EqualTo(ExpectedExtensionId));
            Assert.That(extension.Name, Is.EqualTo("Jellyfin"));
        }
    }
}
