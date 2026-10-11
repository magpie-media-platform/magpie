using System;
using System.Collections.Generic;
using System.Text.Json;

using Magpie.Extensions.Cardigann.Engine;
using Magpie.Extensions.Cardigann.Engine.Filters;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Cardigann.Engine;

[TestFixture]
public sealed class JsonElementExtensionsTests
{
    [TestCase("\"text\"", "text")]
    [TestCase("42", "42")]
    [TestCase("true", "true")]
    [TestCase("null", null)]
    [TestCase("""{"value":42}""", """{"value":42}""")]
    public void ToScalarString_ReturnsExpectedValue_WhenJsonValueIsConverted(string json, string? expected)
    {
        // Arrange
        using var document = JsonDocument.Parse(json);

        // Act
        var result = document.RootElement.ToScalarString();

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void ApplyCardigannFilter_ReturnsJoinedScalars_WhenJsonArrayContainsNull()
    {
        // Arrange
        const string json = """{"items":["text",null,42,true]}""";

        // Act
        var result = json.ApplyCardigannFilter(
            "jsonjoinarray",
            new object[] { "items", "|" },
            new Dictionary<string, object?>(),
            TimeProvider.System);

        // Assert
        Assert.That(result, Is.EqualTo("text||42|true"));
    }
}
