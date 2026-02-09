using System.Linq;

using Magpie.QueryLanguage;

using NUnit.Framework;

namespace Magpie.Test.QueryLanguage;

[TestFixture]
public class FilterQueryTokenizerTests
{
    [Test]
    public void Tokenize_ReturnsEmpty_WhenInputEmpty()
    {
        // Arrange / Act
        var result = FilterQueryTokenizer.TryTokenize(string.Empty);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value)).Empty);
    }

    [Test]
    public void Tokenize_ReturnsEmpty_WhenInputWhitespaceOnly()
    {
        // Arrange / Act
        var result = FilterQueryTokenizer.TryTokenize("   \t\n  ");

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value)).Empty);
    }

    [Test]
    public void Tokenize_ReturnsIdentifierToken_WhenInputIsIdentifier()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("name");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens, Has.Exactly(1).Items);
            Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokens.First().ToStringValue(), Is.EqualTo("name"));
        }
    }

    [Test]
    public void Tokenize_ReturnsIdentifierToken_WhenInputContainsUnderscore()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("field_name");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens, Has.Exactly(1).Items);
            Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokens.First().ToStringValue(), Is.EqualTo("field_name"));
        }
    }

    [Test]
    public void Tokenize_ReturnsIdentifierToken_WhenInputContainsNumbers()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("field123");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens, Has.Exactly(1).Items);
            Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokens.First().ToStringValue(), Is.EqualTo("field123"));
        }
    }

    [Test]
    public void Tokenize_ReturnsStringToken_WhenInputIsQuotedString()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("\"Breaking Bad\"");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens, Has.Exactly(1).Items);
            Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.String));
        }
    }

    [Test]
    public void Tokenize_ReturnsStringToken_WhenInputIsEmptyString()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("\"\"");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens, Has.Exactly(1).Items);
            Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.String));
        }
    }

    [Test]
    public void Tokenize_ReturnsStringToken_WhenInputHasEscapedQuote()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("\"Hello \\\"World\\\"\"");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens, Has.Exactly(1).Items);
            Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.String));
        }
    }

    [Test]
    public void Tokenize_ReturnsStringToken_WhenInputHasEscapedBackslash()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("\"path\\\\to\\\\file\"");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens, Has.Exactly(1).Items);
            Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.String));
        }
    }

    [Test]
    public void Tokenize_ReturnsError_WhenStringUnterminated()
    {
        // Arrange / Act
        var result = FilterQueryTokenizer.TryTokenize("\"unterminated");

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).False);
    }

    [Test]
    public void Tokenize_ReturnsNumberToken_WhenInputIsInteger()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("12345");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens, Has.Exactly(1).Items);
            Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Number));
            Assert.That(tokens.First().ToStringValue(), Is.EqualTo("12345"));
        }
    }

    [Test]
    public void Tokenize_ReturnsNumberToken_WhenInputIsDecimal()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("123.45");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens, Has.Exactly(1).Items);
            Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Number));
            Assert.That(tokens.First().ToStringValue(), Is.EqualTo("123.45"));
        }
    }

    [Test]
    public void Tokenize_ReturnsNumberToken_WhenInputIsNegative()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("-123");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens, Has.Exactly(1).Items);
            Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Number));
            Assert.That(tokens.First().ToStringValue(), Is.EqualTo("-123"));
        }
    }

    [Test]
    public void Tokenize_ReturnsParenTokens_WhenInputIsParentheses()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("()");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens, Has.Exactly(2).Items);
            Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.LeftParen));
            Assert.That(tokens.Skip(1).First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.RightParen));
        }
    }

    [Test]
    public void Tokenize_ReturnsCommaToken_WhenInputIsComma()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize(",");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens, Has.Exactly(1).Items);
            Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Comma));
        }
    }

    [Test]
    public void Tokenize_ReturnsEqualsToken_WhenInputIsEquals()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("=");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.Equals));
    }

    [Test]
    public void Tokenize_ReturnsNotEqualsToken_WhenInputIsNotEquals()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("!=");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.NotEquals));
    }

    [Test]
    public void Tokenize_ReturnsGreaterThanToken_WhenInputIsGreaterThan()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize(">");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.GreaterThan));
    }

    [Test]
    public void Tokenize_ReturnsGteToken_WhenInputIsGreaterThanOrEqual()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize(">=");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.GreaterThanOrEqual));
    }

    [Test]
    public void Tokenize_ReturnsLessThanToken_WhenInputIsLessThan()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("<");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.LessThan));
    }

    [Test]
    public void Tokenize_ReturnsLteToken_WhenInputIsLessThanOrEqual()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("<=");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.LessThanOrEqual));
    }

    [Test]
    public void Tokenize_ReturnsAndToken_WhenInputIsAndKeyword()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("and");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.And));
    }

    [Test]
    public void Tokenize_ParsesAndKeyword_CaseInsensitive()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("AND");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.And));
    }

    [Test]
    public void Tokenize_ReturnsOrToken_WhenInputIsOrKeyword()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("or");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.Or));
    }

    [Test]
    public void Tokenize_ReturnsNotToken_WhenInputIsNotKeyword()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("not");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.Not));
    }

    [Test]
    public void Tokenize_ReturnsHasToken_WhenInputIsHasKeyword()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("has");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.Has));
    }

    [Test]
    public void Tokenize_ReturnsNotHasToken_WhenInputIsNotHas()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("!has ");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.NotHas));
    }

    [Test]
    public void Tokenize_ParsesNotHasOperator_BeforeLeftParen()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("!has(");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens, Has.Exactly(2).Items);
            Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.NotHas));
            Assert.That(tokens.Skip(1).First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.LeftParen));
        }
    }

    [Test]
    public void Tokenize_ReturnsContainsToken_WhenInputIsContains()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("contains");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.Contains));
    }

    [Test]
    public void Tokenize_ReturnsNotContainsToken_WhenInputIsNotContains()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("!contains ");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.NotContains));
    }

    [Test]
    public void Tokenize_ReturnsStartsWithToken_WhenInputIsStartsWith()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("startswith");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.StartsWith));
    }

    [Test]
    public void Tokenize_ReturnsNotStartsWithToken_WhenInputIsNotStartsWith()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("!startswith ");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.NotStartsWith));
    }

    [Test]
    public void Tokenize_ReturnsEndsWithToken_WhenInputIsEndsWith()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("endswith");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.EndsWith));
    }

    [Test]
    public void Tokenize_ReturnsNotEndsWithToken_WhenInputIsNotEndsWith()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("!endswith ");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.NotEndsWith));
    }

    [Test]
    public void Tokenize_ReturnsInToken_WhenInputIsInKeyword()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("in");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.In));
    }

    [Test]
    public void Tokenize_ReturnsNotInToken_WhenInputIsNotIn()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("!in ");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.NotIn));
    }

    [Test]
    public void Tokenize_ParsesNotInOperator_BeforeLeftParen()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("!in(");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokens, Has.Exactly(2).Items);
            Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.NotIn));
            Assert.That(tokens.Skip(1).First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.LeftParen));
        }
    }

    [Test]
    public void Tokenize_ReturnsNullToken_WhenInputIsNullKeyword()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("null");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.Null));
    }

    [Test]
    public void Tokenize_ReturnsNotNullToken_WhenInputIsNotNull()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("!null");

        // Assert
        Assert.That(tokens.First(), Has.Property(nameof(Superpower.Model.Token<>.Kind))
            .EqualTo(FilterQueryToken.NotNull));
    }

    [Test]
    public void Tokenize_ReturnsError_WhenUnexpectedCharacter()
    {
        // Arrange / Act
        var result = FilterQueryTokenizer.TryTokenize("name @ value");

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).False);
    }

    [Test]
    public void Tokenize_ReturnsAllTokens_WhenInputIsSimpleExpression()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("name = \"Breaking Bad\"");

        // Assert
        var tokenList = tokens.ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokenList, Has.Exactly(3).Items);
            Assert.That(tokenList[0], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokenList[0].ToStringValue(), Is.EqualTo("name"));
            Assert.That(tokenList[1], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Equals));
            Assert.That(tokenList[2], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.String));
        }
    }

    [Test]
    public void Tokenize_ReturnsAllTokens_WhenInputIsCompoundExpression()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("monitoring = Active and contentType = \"show\"");

        // Assert
        var tokenList = tokens.ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokenList, Has.Exactly(7).Items);
            Assert.That(tokenList[0], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokenList[1], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Equals));
            Assert.That(tokenList[2], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokenList[3], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.And));
            Assert.That(tokenList[4], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokenList[5], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Equals));
            Assert.That(tokenList[6], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.String));
        }
    }

    [Test]
    public void Tokenize_ReturnsAllTokens_WhenInputIsCollectionExpression()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("genre has \"Action\"");

        // Assert
        var tokenList = tokens.ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokenList, Has.Exactly(3).Items);
            Assert.That(tokenList[0], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokenList[0].ToStringValue(), Is.EqualTo("genre"));
            Assert.That(tokenList[1], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Has));
            Assert.That(tokenList[2], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.String));
        }
    }

    [Test]
    public void Tokenize_ReturnsAllTokens_WhenInputIsComplexCollectionExpr()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("genres has (name startswith \"A\" and name endswith \"ion\")");

        // Assert
        var tokenList = tokens.ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokenList, Has.Exactly(11).Items);
            Assert.That(tokenList[0], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokenList[1], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Has));
            Assert.That(tokenList[2], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.LeftParen));
            Assert.That(tokenList[3], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokenList[4], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.StartsWith));
            Assert.That(tokenList[5], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.String));
            Assert.That(tokenList[6], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.And));
            Assert.That(tokenList[7], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokenList[8], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.EndsWith));
            Assert.That(tokenList[9], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.String));
            Assert.That(tokenList[10], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.RightParen));
        }
    }

    [Test]
    public void Tokenize_ReturnsAllTokens_WhenInputIsInWithList()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("contentType in (\"movie\", \"show\")");

        // Assert
        var tokenList = tokens.ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokenList, Has.Exactly(7).Items);
            Assert.That(tokenList[0], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokenList[1], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.In));
            Assert.That(tokenList[2], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.LeftParen));
            Assert.That(tokenList[3], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.String));
            Assert.That(tokenList[4], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Comma));
            Assert.That(tokenList[5], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.String));
            Assert.That(tokenList[6], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.RightParen));
        }
    }

    [Test]
    public void Tokenize_ReturnsNullToken_WhenUsedInExpression()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("name null");

        // Assert
        var tokenList = tokens.ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokenList, Has.Exactly(2).Items);
            Assert.That(tokenList[0], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokenList[1], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Null));
        }
    }

    [Test]
    public void Tokenize_ReturnsNotNullToken_WhenUsedInExpression()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("name !null");

        // Assert
        var tokenList = tokens.ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokenList, Has.Exactly(2).Items);
            Assert.That(tokenList[0], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokenList[1], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.NotNull));
        }
    }

    [Test]
    public void Tokenize_ReturnsIdentifierToken_WhenInputIsBoolean()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("won = true");

        // Assert
        var tokenList = tokens.ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokenList, Has.Exactly(3).Items);
            Assert.That(tokenList[0], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokenList[2], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokenList[2].ToStringValue(), Is.EqualTo("true"));
        }
    }

    [Test]
    public void Tokenize_ReturnsIdentifierToken_WhenInputIsEnumValue()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("monitoring = Active");

        // Assert
        var tokenList = tokens.ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokenList, Has.Exactly(3).Items);
            Assert.That(tokenList[2], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Identifier));
            Assert.That(tokenList[2].ToStringValue(), Is.EqualTo("Active"));
        }
    }

    [Test]
    public void Tokenize_ReturnsNumberToken_WhenInputIsLargeNumber()
    {
        // Arrange / Act
        var tokens = FilterQueryTokenizer.Tokenize("fileSize > 1000000000");

        // Assert
        var tokenList = tokens.ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tokenList, Has.Exactly(3).Items);
            Assert.That(tokenList[2], Has.Property(nameof(Superpower.Model.Token<>.Kind))
                .EqualTo(FilterQueryToken.Number));
            Assert.That(tokenList[2].ToStringValue(),
                Is.EqualTo("1000000000"));
        }
    }
}
