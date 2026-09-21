using System;

using Magpie.Models.Signals;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class SignalBagTests
{
    [TestCase("key")]
    [TestCase(" KEY ")]
    public void GetFirstValue_ReturnsValue_WhenKeyHasDifferentWhitespaceOrCase(string key)
    {
        // Arrange
        var signals = new SignalBagBuilder().AddValue(" key ", " value ").Build();

        // Act
        var value = signals.GetFirstValue(key);

        // Assert
        Assert.That(value, Is.EqualTo("value"));
    }

    [TestCase("")]
    [TestCase(" ")]
    public void AddValueAndGetValues_Throw_WhenKeyIsEmpty(string key)
    {
        // Arrange
        var builder = new SignalBagBuilder();
        var signals = builder.Build();

        // Act
        var add = () => builder.AddValue(key, "value");
        var read = () => signals.GetValues(key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(add, Throws.TypeOf<ArgumentException>());
            Assert.That(read, Throws.TypeOf<ArgumentException>());
        }
    }
}
