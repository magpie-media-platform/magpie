using System;

using Magpie.Core.Extensions;

using NUnit.Framework;

namespace Magpie.Test.Core.Extensions;

[TestFixture]
public sealed class AbiVersionExtensionsTests
{
    [TestCase("1.4.2", "1.4.2", null)]
    [TestCase("1.9.0", "1.4.2", null)]
    [TestCase("1.4.3", "1.4.2", "")]
    [TestCase("1.9.0", "1.4.0", "[1.0.0,)")]
    [TestCase("1.5.3", "1.4.0", "[1.5.0,1.6.0)")]
    [TestCase("1.4.2", "1.4.0", "1.4.2")]
    [TestCase("1.4.2", "1.4.0", "[1.4.2]")]
    [TestCase("0.0.0", "0.0.0", null)]
    public void IsCompatibleWith_ReturnsTrue_WhenHostSatisfiesTheDefaultAndTargetAbi(
        string host,
        string builtAgainst,
        string? targetAbi)
    {
        // Arrange
        var currentAbi = new Version(host);

        // Act
        var compatible = currentAbi.IsCompatibleWith(new Version(builtAgainst), targetAbi);

        // Assert
        Assert.That(compatible, Is.True);
    }

    [TestCase("2.0.0", "1.4.2", null)]
    [TestCase("2.0.0", "1.0.0", "[1.0.0,)")]
    [TestCase("0.9.0", "1.0.0", null)]
    [TestCase("1.4.1", "1.4.2", null)]
    [TestCase("1.0.0", "0.0.0", null)]
    [TestCase("1.6.0", "1.4.0", "[1.4.0,1.6.0)")]
    [TestCase("1.4.3", "1.4.0", "1.4.2")]
    [TestCase("1.4.2", "1.4.0", "(,1.4.2)")]
    [TestCase("1.4.2", "1.4.0", "not-a-version")]
    public void IsCompatibleWith_ReturnsFalse_WhenHostFailsTheDefaultOrTargetAbi(
        string host,
        string builtAgainst,
        string? targetAbi)
    {
        // Arrange
        var currentAbi = new Version(host);

        // Act
        var compatible = currentAbi.IsCompatibleWith(new Version(builtAgainst), targetAbi);

        // Assert
        Assert.That(compatible, Is.False);
    }
}
