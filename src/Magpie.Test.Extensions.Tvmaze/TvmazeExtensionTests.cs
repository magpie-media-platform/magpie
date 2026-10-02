using System;

using Magpie.Extensions.Tvmaze;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tvmaze;

[TestFixture]
public class TvmazeExtensionTests
{
    private static readonly Guid ExpectedExtensionId = new("055D58FA-9A32-42CA-A194-A3CE12C7E40E");

    [Test]
    public void Id_ReturnsStableIdentity_WhenExtensionIsCreated()
    {
        // Arrange
        var extension = new TvmazeExtension();

        // Act
        var id = extension.Id;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(id, Is.EqualTo(ExpectedExtensionId));
            Assert.That(extension.Name, Is.EqualTo("TVmaze"));
        }
    }
}
