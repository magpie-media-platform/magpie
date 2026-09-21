using System;
using System.Globalization;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class TemporalValueTests
{
    [TestCase("2024-3-7")]
    [TestCase("2024-05-06Z")]
    [TestCase("2024-05-06 Z")]
    [TestCase("2024-05-06 +02:00")]
    [TestCase("2024-05-06T12:00:00")]
    public void TryParse_ReturnsFalse_WhenInputHasNoSupportedPrecision(string text)
    {
        // Act
        var success = TemporalValue.TryParse(text, out var value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success, Is.False);
            Assert.That(value, Is.Null);
        }
    }

    [TestCase("2024", TemporalPrecision.Year, "2024-01-01T00:00:00.0000000Z")]
    [TestCase("2024-05", TemporalPrecision.Month, "2024-05-01T00:00:00.0000000Z")]
    [TestCase("2024-05-06", TemporalPrecision.Day, "2024-05-06T00:00:00.0000000Z")]
    [TestCase("2024-05-06T12:00:00Z", TemporalPrecision.Instant, "2024-05-06T12:00:00.0000000Z")]
    [TestCase("2024-05-06 12:00:00+02:00", TemporalPrecision.Instant, "2024-05-06T10:00:00.0000000Z")]
    [TestCase("2024-05-06T12:00:00-02:00", TemporalPrecision.Instant, "2024-05-06T14:00:00.0000000Z")]
    public void TryParse_PreservesPrecision_WhenInputIsSupported(
        string text,
        TemporalPrecision precision,
        string expected)
    {
        // Act
        var success = TemporalValue.TryParse(text, out var value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success, Is.True);
            Assert.That(value?.Precision, Is.EqualTo(precision));
            Assert.That(value?.Value.ToString("O", CultureInfo.InvariantCulture), Is.EqualTo(expected));
        }
    }

    [TestCase(CriteriaValueOperator.ContainsString)]
    [TestCase(CriteriaValueOperator.NotContainsString)]
    [TestCase(CriteriaValueOperator.StartsWithString)]
    [TestCase(CriteriaValueOperator.NotStartsWithString)]
    [TestCase(CriteriaValueOperator.EndsWithString)]
    [TestCase(CriteriaValueOperator.NotEndsWithString)]
    [TestCase(CriteriaValueOperator.Regex)]
    [TestCase(CriteriaValueOperator.ContainsWord)]
    [TestCase(CriteriaValueOperator.IsNull)]
    [TestCase(CriteriaValueOperator.IsNotNull)]
    public void Compare_ReturnsTrue_WhenOperatorIsUnsupported(CriteriaValueOperator operation)
    {
        // Arrange
        var candidate = new TemporalValue
        {
            Value = new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc),
            Precision = TemporalPrecision.Day
        };

        // Act
        var single = candidate.Compare(operation, candidate);
        var multiple = candidate.Compare(operation, [candidate]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(single, Is.True);
            Assert.That(multiple, Is.True);
        }
    }

    [TestCase(CriteriaValueOperator.Equals, true)]
    [TestCase(CriteriaValueOperator.GreaterThanOrEqual, true)]
    [TestCase(CriteriaValueOperator.LessThanOrEqual, true)]
    [TestCase(CriteriaValueOperator.In, true)]
    [TestCase(CriteriaValueOperator.NotIn, false)]
    public void Compare_UsesTheFirstValue_WhenAScalarOperatorGetsMultipleValues(
        CriteriaValueOperator operation,
        bool expected)
    {
        // Arrange
        var candidate = new TemporalValue
        {
            Value = new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc),
            Precision = TemporalPrecision.Day
        };
        var other = new TemporalValue
        {
            Value = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Precision = TemporalPrecision.Day
        };

        // Act
        var matches = candidate.Compare(operation, [candidate, other]);

        // Assert
        Assert.That(matches, Is.EqualTo(expected));
    }
}
