using System;

using Magpie.Extensions.Movies;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Movies;

[TestFixture]
public class MoviesExtensionTests
{
    private static readonly Guid ExpectedExtensionId = new("9719FAC3-1DA3-4BA8-816F-28D0A8F1AF6F");

    [Test]
    public void Identity_ReturnsStableValue_WhenRead()
    {
        // Arrange

        // Act
        var extension = new MoviesExtension();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(extension.Id, Is.EqualTo(ExpectedExtensionId));
            Assert.That(extension.Name, Is.EqualTo("Movies"));
            Assert.That(extension.Version, Is.EqualTo(new Version(1, 0, 0)));
            Assert.That(extension.TargetABI, Is.EqualTo("[1.0.0,)"));
        }
    }
}
