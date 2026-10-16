using Microsoft.AspNetCore.Builder;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class NewInstanceTests
{
    [TestCase(false, 0, false, true)]
    [TestCase(true, 0, false, true)]
    [TestCase(true, 0, true, false)]
    [TestCase(true, 3, false, false)]
    [TestCase(true, 3, true, false)]
    public void IsNewInstance_ReturnsTrue_WhenNoExtensionWasEverRegistered(
        bool hasSchema,
        long activationGeneration,
        bool hasExtensions,
        bool expected)
    {
        // Act
        var newInstance = MagpieHostBuilderExtensions.IsNewInstance(hasSchema, activationGeneration, hasExtensions);

        // Assert
        Assert.That(newInstance, Is.EqualTo(expected));
    }
}
