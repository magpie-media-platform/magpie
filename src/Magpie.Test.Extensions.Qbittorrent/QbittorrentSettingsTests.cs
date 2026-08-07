using System.Collections.Generic;

using Magpie.Extensions.Qbittorrent;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Qbittorrent;

[TestFixture]
public class QbittorrentSettingsTests
{
    [TestCase("true", true)]
    [TestCase("True", true)]
    [TestCase("TRUE", true)]
    [TestCase("1", true)]
    [TestCase("false", false)]
    [TestCase("False", false)]
    [TestCase("FALSE", false)]
    [TestCase("0", false)]
    public void Parse_AddPaused_WhenPausedUsesBooleanLexeme(string paused, bool expected)
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["host"] = "http://localhost:8080",
            ["paused"] = paused
        };

        // Act
        var parsed = QbittorrentSettings.Parse(settings);

        // Assert
        Assert.That(parsed.AddPaused, Is.EqualTo(expected));
    }

    [Test]
    public void Parse_DefaultCategory_WhenCategoryIsUnset()
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["host"] = "http://localhost:8080"
        };

        // Act
        var parsed = QbittorrentSettings.Parse(settings);

        // Assert
        Assert.That(parsed.Category, Is.EqualTo("magpie"));
    }

    [Test]
    public void Parse_DefaultCategory_WhenCategoryIsBlank()
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["host"] = "http://localhost:8080",
            ["category"] = "   "
        };

        // Act
        var parsed = QbittorrentSettings.Parse(settings);

        // Assert
        Assert.That(parsed.Category, Is.EqualTo("magpie"));
    }

    [Test]
    public void Parse_ConfiguredCategory_WhenCategoryIsPresent()
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["host"] = "http://localhost:8080",
            ["category"] = "films"
        };

        // Act
        var parsed = QbittorrentSettings.Parse(settings);

        // Assert
        Assert.That(parsed.Category, Is.EqualTo("films"));
    }

    [Test]
    public void Parse_FalseAddPaused_WhenPausedIsUnrecognized()
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["host"] = "http://localhost:8080",
            ["paused"] = "yes"
        };

        // Act
        var parsed = QbittorrentSettings.Parse(settings);

        // Assert
        Assert.That(parsed.AddPaused, Is.False);
    }
}
