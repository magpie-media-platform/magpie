using System.Linq;

using Magpie.QueryLanguage;

using NUnit.Framework;

namespace Magpie.Test.QueryLanguage;

[TestFixture]
internal sealed class QueryNodesTests
{
    [TestCase(
        "Title >= -12.5",
        new[] { FilterQueryToken.Identifier, FilterQueryToken.GreaterThanOrEqual, FilterQueryToken.Number })]
    [TestCase(
        "Title !contains \"rock and roll\"",
        new[] { FilterQueryToken.Identifier, FilterQueryToken.NotContains, FilterQueryToken.String })]
    [TestCase(
        "Title = \"x\" AnD Language = \"en\"",
        new[]
        {
            FilterQueryToken.Identifier,
            FilterQueryToken.Equals,
            FilterQueryToken.String,
            FilterQueryToken.And,
            FilterQueryToken.Identifier,
            FilterQueryToken.Equals,
            FilterQueryToken.String
        })]
    public void TryTokenize_ReturnsTokens_WhenQueryContainsOperators(string query, FilterQueryToken[] expected)
    {
        // Arrange

        // Act
        var result = query.TryTokenize();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.HasValue, Is.True);
            Assert.That(result.Value.Select(token => token.Kind), Is.EqualTo(expected));
        }
    }

    [TestCase("Title = \"unterminated")]
    [TestCase("Title = @")]
    public void TryTokenize_ReturnsFailure_WhenTokenIsMalformed(string query)
    {
        // Arrange

        // Act
        var result = query.TryTokenize();

        // Assert
        Assert.That(result.HasValue, Is.False);
    }

    [Test]
    public void ParseFilterQuery_GroupsAndBeforeOr_WhenOperatorsAreMixed()
    {
        // Arrange
        var query = "Title = \"one\" or Title = \"two\" and Language = \"en\"";

        // Act
        var result = query.ParseFilterQuery();

        // Assert
        var root = (BinaryNode)result.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.Operator, Is.EqualTo(BinaryOperator.Or));
            Assert.That(((ScalarExprNode)root.Left).Field, Is.EqualTo("Title"));
            Assert.That(((BinaryNode)root.Right).Operator, Is.EqualTo(BinaryOperator.And));
            Assert.That(((ScalarExprNode)((BinaryNode)root.Right).Right).Field, Is.EqualTo("Language"));
        }
    }

    [TestCase("")]
    [TestCase("Title =")]
    [TestCase("(Title = \"one\"")]
    [TestCase("Title in ()")]
    [TestCase("Title = \"one\" trailing")]
    public void ParseContentFilter_ReturnsFailure_WhenQueryIsIncomplete(string query)
    {
        // Arrange

        // Act
        var result = query.ParseContentFilter();

        // Assert
        Assert.That(result.IsSuccess, Is.False);
    }

    [TestCase("Title = \"one\"", "Title = \"one\"")]
    [TestCase("title contains \"rock and roll\"", "Title contains \"rock and roll\"")]
    [TestCase("Title = \"a\\\"b\"", "Title = \"a\\\"b\"")]
    public void FormatContentFilter_PreservesValues_WhenQueryIsParsed(string query, string expected)
    {
        // Arrange
        var criteria = query.ParseContentFilter();

        // Act
        var formatted = criteria.Value!.FormatContentFilter();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(criteria.IsSuccess, Is.True);
            Assert.That(formatted, Is.EqualTo(expected));
        }
    }
}
