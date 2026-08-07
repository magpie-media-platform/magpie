using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models.Enums;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class CriteriaMatcherTests
{
    [TestCase(CriteriaValueOperator.Equals, "The Matrix", "the matrix")]
    [TestCase(CriteriaValueOperator.ContainsString, "The Matrix", "MATRIX")]
    [TestCase(CriteriaValueOperator.ContainsWord, "The Office", "OFFICE")]
    [TestCase(CriteriaValueOperator.StartsWithString, "The Matrix", "THE")]
    [TestCase(CriteriaValueOperator.EndsWithString, "The Matrix", "MATRIX")]
    [TestCase(CriteriaValueOperator.Regex, "The Matrix", "^the matrix$")]
    public void Match_Matched_WhenPositiveOperatorMatchesIgnoringCase(
        CriteriaValueOperator valueOperator,
        string actual,
        string value)
    {
        // Act
        var result = CriteriaMatcher.Match(actual, valueOperator, [value]);

        // Assert
        Assert.That(result, Is.EqualTo(CriteriaMatchResult.Matched));
    }

    [Test]
    public void Match_Matched_WhenInContainsCaseInsensitiveValue()
    {
        // Act
        var result = CriteriaMatcher.Match("The Matrix", CriteriaValueOperator.In, ["Arrival", "the matrix"]);

        // Assert
        Assert.That(result, Is.EqualTo(CriteriaMatchResult.Matched));
    }

    [TestCase(CriteriaValueOperator.ContainsString, "mél")]
    [TestCase(CriteriaValueOperator.ContainsWord, "Amélie")]
    [TestCase(CriteriaValueOperator.StartsWithString, "Amé")]
    [TestCase(CriteriaValueOperator.EndsWithString, "élie")]
    public void Match_Matched_WhenTextOperatorMatchesLaterAlternative(
        CriteriaValueOperator valueOperator,
        string matchingValue)
    {
        // Act
        var result = CriteriaMatcher.Match("Amélie", valueOperator, ["The Matrix", matchingValue]);

        // Assert
        Assert.That(result, Is.EqualTo(CriteriaMatchResult.Matched));
    }

    [TestCase("The-Office", CriteriaMatchResult.Matched)]
    [TestCase("The_Office", CriteriaMatchResult.Rejected)]
    [TestCase("Officeholder", CriteriaMatchResult.Rejected)]
    public void Match_Result_WhenContainsWordBoundaryVaries(
        string actual,
        CriteriaMatchResult expected)
    {
        // Act
        var result = CriteriaMatcher.Match(actual, CriteriaValueOperator.ContainsWord, ["office"]);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(null, CriteriaValueOperator.IsNull, CriteriaMatchResult.Matched)]
    [TestCase("", CriteriaValueOperator.IsNull, CriteriaMatchResult.Matched)]
    [TestCase("The Matrix", CriteriaValueOperator.IsNull, CriteriaMatchResult.Rejected)]
    [TestCase(null, CriteriaValueOperator.IsNotNull, CriteriaMatchResult.Rejected)]
    [TestCase("The Matrix", CriteriaValueOperator.IsNotNull, CriteriaMatchResult.Matched)]
    public void Match_Result_WhenNullOperatorEvaluates(
        string? actual,
        CriteriaValueOperator valueOperator,
        CriteriaMatchResult expected)
    {
        // Act
        var result = CriteriaMatcher.Match(actual, valueOperator, []);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(CriteriaValueOperator.NotEquals, "The Matrix", "the matrix")]
    [TestCase(CriteriaValueOperator.NotIn, "The Matrix", "the matrix")]
    [TestCase(CriteriaValueOperator.NotContainsString, "The Matrix", "matrix")]
    [TestCase(CriteriaValueOperator.NotStartsWithString, "The Matrix", "the")]
    [TestCase(CriteriaValueOperator.NotEndsWithString, "The Matrix", "matrix")]
    public void Match_Rejected_WhenExplicitNegativeOperatorRejects(
        CriteriaValueOperator valueOperator,
        string actual,
        string value)
    {
        // Act
        var result = CriteriaMatcher.Match(actual, valueOperator, [value]);

        // Assert
        Assert.That(result, Is.EqualTo(CriteriaMatchResult.Rejected));
    }

    [TestCase(CriteriaValueOperator.NotEquals)]
    [TestCase(CriteriaValueOperator.NotIn)]
    [TestCase(CriteriaValueOperator.NotContainsString)]
    [TestCase(CriteriaValueOperator.NotStartsWithString)]
    [TestCase(CriteriaValueOperator.NotEndsWithString)]
    public void Match_Matched_WhenNegateDoubleInvertsExplicitNegativeOperator(
        CriteriaValueOperator valueOperator)
    {
        // Act
        var result = CriteriaMatcher.Match("The Matrix", valueOperator, ["The Matrix"], negate: true);

        // Assert
        Assert.That(result, Is.EqualTo(CriteriaMatchResult.Matched));
    }

    [Test]
    public void Match_Rejected_WhenNegateInvertsPositiveMatch()
    {
        // Act
        var result = CriteriaMatcher.Match(
            "The Matrix",
            CriteriaValueOperator.ContainsString,
            ["matrix"],
            negate: true);

        // Assert
        Assert.That(result, Is.EqualTo(CriteriaMatchResult.Rejected));
    }

    [Test]
    public void Match_Unverifiable_WhenActualValueIsMissing()
    {
        // Act
        var result = CriteriaMatcher.Match(null, CriteriaValueOperator.Equals, ["The Matrix"]);

        // Assert
        Assert.That(result, Is.EqualTo(CriteriaMatchResult.Unverifiable));
    }

    [Test]
    public void Match_Unverifiable_WhenComparisonValueIsMissing()
    {
        // Act
        var result = CriteriaMatcher.Match("The Matrix", CriteriaValueOperator.Equals, []);

        // Assert
        Assert.That(result, Is.EqualTo(CriteriaMatchResult.Unverifiable));
    }

    [Test]
    public void Match_Unverifiable_WhenRegexIsInvalid()
    {
        // Act
        var result = CriteriaMatcher.Match("The Matrix", CriteriaValueOperator.Regex, ["["]);

        // Assert
        Assert.That(result, Is.EqualTo(CriteriaMatchResult.Unverifiable));
    }

    [Test]
    public void Match_Unverifiable_WhenRegexTimesOut()
    {
        // Arrange
        var actual = new string('a', 10_000) + "!";

        // Act
        var result = CriteriaMatcher.Match(actual, CriteriaValueOperator.Regex, ["^(a+)+$"]);

        // Assert
        Assert.That(result, Is.EqualTo(CriteriaMatchResult.Unverifiable));
    }

    [Test]
    public void Match_Unverifiable_WhenOperatorIsUnsupported()
    {
        // Act
        var result = CriteriaMatcher.Match("The Matrix", CriteriaValueOperator.GreaterThan, ["Arrival"]);

        // Assert
        Assert.That(result, Is.EqualTo(CriteriaMatchResult.Unverifiable));
    }
}
