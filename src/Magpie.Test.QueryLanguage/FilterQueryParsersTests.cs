using System.Collections.Generic;

using Magpie.QueryLanguage;

using NUnit.Framework;

using Superpower;

namespace Magpie.Test.QueryLanguage;

[TestFixture]
public class FilterQueryParsersTests
{
    [Test]
    public void StringValue_ReturnsStringNode_WhenInputIsQuotedString()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("\"Breaking Bad\"");

        // Act
        var result = FilterQueryParsers.StringValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value)).TypeOf<StringValueNode>());

        var node = (StringValueNode)result.Value;
        Assert.That(node, Has.Property(nameof(node.Value)).EqualTo("Breaking Bad"));
    }

    [Test]
    public void StringValue_ReturnsEmptyString_WhenInputIsEmptyQuotes()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("\"\"");

        // Act
        var result = FilterQueryParsers.StringValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (StringValueNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Value)).EqualTo(string.Empty));
    }

    [Test]
    public void StringValue_UnescapesQuote_WhenInputContainsEscapedQuote()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("\"Hello \\\"World\\\"\"");

        // Act
        var result = FilterQueryParsers.StringValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (StringValueNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Value)).EqualTo("Hello \"World\""));
    }

    [Test]
    public void StringValue_UnescapesBackslash_WhenInputContainsEscapedBackslash()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("\"path\\\\to\\\\file\"");

        // Act
        var result = FilterQueryParsers.StringValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (StringValueNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Value)).EqualTo("path\\to\\file"));
    }

    [Test]
    public void StringValue_UnescapesNewlineAndTab_WhenInputContainsEscapeSequences()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("\"line1\\nline2\\ttab\"");

        // Act
        var result = FilterQueryParsers.StringValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (StringValueNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Value))
                .EqualTo("line1\nline2\ttab"));
    }

    [Test]
    public void StringValue_TracksPosition_WhenInputIsParsed()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("\"test\"");

        // Act
        var result = FilterQueryParsers.StringValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (StringValueNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Position)).EqualTo(0));
            Assert.That(node,
                Has.Property(nameof(node.Length)).EqualTo(6));
        }
    }

    [Test]
    public void NumberValue_ReturnsNumberNode_WhenInputIsInteger()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("12345");

        // Act
        var result = FilterQueryParsers.NumberValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value)).TypeOf<NumberValueNode>());

        var node = (NumberValueNode)result.Value;
        Assert.That(node, Has.Property(nameof(node.Value)).EqualTo("12345"));
    }

    [Test]
    public void NumberValue_ReturnsNumberNode_WhenInputIsDecimal()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("123.45");

        // Act
        var result = FilterQueryParsers.NumberValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (NumberValueNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Value)).EqualTo("123.45"));
    }

    [Test]
    public void NumberValue_ReturnsNumberNode_WhenInputIsNegative()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("-42");

        // Act
        var result = FilterQueryParsers.NumberValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (NumberValueNode)result.Value;
        Assert.That(node, Has.Property(nameof(node.Value)).EqualTo("-42"));
    }

    [Test]
    public void NumberValue_TracksPosition_WhenInputIsParsed()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("999");

        // Act
        var result = FilterQueryParsers.NumberValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (NumberValueNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Position)).EqualTo(0));
            Assert.That(node,
                Has.Property(nameof(node.Length)).EqualTo(3));
        }
    }

    [Test]
    public void IdentifierValue_ReturnsIdentifierNode_WhenInputIsIdentifier()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("Active");

        // Act
        var result = FilterQueryParsers.IdentifierValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value))
                .TypeOf<IdentifierValueNode>());

        var node = (IdentifierValueNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Value)).EqualTo("Active"));
    }

    [Test]
    public void IdentifierValue_ReturnsIdentifierNode_WhenInputIsBoolean()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("true");

        // Act
        var result = FilterQueryParsers.IdentifierValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (IdentifierValueNode)result.Value;
        Assert.That(node, Has.Property(nameof(node.Value)).EqualTo("true"));
    }

    [Test]
    public void IdentifierValue_TracksPosition_WhenInputIsParsed()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("Monitored");

        // Act
        var result = FilterQueryParsers.IdentifierValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (IdentifierValueNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Position)).EqualTo(0));
            Assert.That(node,
                Has.Property(nameof(node.Length)).EqualTo(9));
        }
    }

    [Test]
    public void Value_ReturnsStringNode_WhenInputIsString()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("\"test\"");

        // Act
        var result = FilterQueryParsers.Value.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value)).TypeOf<StringValueNode>());
    }

    [Test]
    public void Value_ReturnsNumberNode_WhenInputIsNumber()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("42");

        // Act
        var result = FilterQueryParsers.Value.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value)).TypeOf<NumberValueNode>());
    }

    [Test]
    public void Value_ReturnsIdentifierNode_WhenInputIsIdentifier()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("Active");

        // Act
        var result = FilterQueryParsers.Value.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value))
                .TypeOf<IdentifierValueNode>());
    }

    [Test]
    public void ValueList_ReturnsSingleItem_WhenInputHasOneValue()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("(\"movie\")");

        // Act
        var result = FilterQueryParsers.ValueList.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value)).TypeOf<ListValueNode>());

        var node = (ListValueNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Values)).Exactly(1).Items);
    }

    [Test]
    public void ValueList_ReturnsMultipleItems_WhenInputHasMultipleStrings()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("(\"movie\", \"show\", \"episode\")");

        // Act
        var result = FilterQueryParsers.ValueList.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ListValueNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Values)).Exactly(3).Items);
    }

    [Test]
    public void ValueList_ReturnsIdentifierNodes_WhenInputHasMultipleIdentifiers()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("(Active, Ended, Cancelled)");

        // Act
        var result = FilterQueryParsers.ValueList.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ListValueNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Values)).Exactly(3).Items);
            Assert.That(node,
                Has.Property(nameof(node.Values))
                    .All.TypeOf<IdentifierValueNode>());
        }
    }

    [Test]
    public void ValueList_ReturnsMixedNodes_WhenInputHasMixedTypes()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("(\"test\", 42, Active)");

        // Act
        var result = FilterQueryParsers.ValueList.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ListValueNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Values)).Exactly(3).Items);
            Assert.That(node,
                Has.Property(nameof(node.Values))
                    .Matches<IReadOnlyList<ValueNode>>(v =>
                        v[0] is StringValueNode
                        && v[1] is NumberValueNode
                        && v[2] is IdentifierValueNode));
        }
    }

    [Test]
    public void ValueList_TracksPosition_WhenInputIsParsed()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("(\"a\", \"b\")");

        // Act
        var result = FilterQueryParsers.ValueList.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ListValueNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Position)).EqualTo(0));
            Assert.That(node,
                Has.Property(nameof(node.Length)).EqualTo(10));
        }
    }

    [Test]
    public void ValueList_ReturnsError_WhenEmpty()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("()");

        // Act
        var result = FilterQueryParsers.ValueList.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).False);
    }

    [Test]
    public void ValueList_ReturnsError_WhenMissingRightParen()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("(\"a\", \"b\"");

        // Act
        var result = FilterQueryParsers.ValueList.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).False);
    }

    [Test]
    public void CollectionOp_ReturnsHas_WhenInputIsHas()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("has");

        // Act
        var result = FilterQueryParsers.CollectionOp.TryParse(tokens);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result,
                Has.Property(nameof(result.HasValue)).True);
            Assert.That(result,
                Has.Property(nameof(result.Value))
                    .EqualTo(CollectionOperator.Has));
        }
    }

    [Test]
    public void CollectionOp_ReturnsNotHas_WhenInputIsNotHas()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("!has ");

        // Act
        var result = FilterQueryParsers.CollectionOp.TryParse(tokens);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result,
                Has.Property(nameof(result.HasValue)).True);
            Assert.That(result,
                Has.Property(nameof(result.Value))
                    .EqualTo(CollectionOperator.NotHas));
        }
    }

    [Test]
    public void CollectionOp_ReturnsError_WhenNotCollectionOperator()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("=");

        // Act
        var result = FilterQueryParsers.CollectionOp.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).False);
    }

    [Test]
    public void ScalarExpr_ReturnsScalarNode_WhenOperatorIsEquals()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("name = \"Breaking Bad\"");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value)).TypeOf<ScalarExprNode>());

        var node = (ScalarExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field)).EqualTo("name"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.Equals));
            Assert.That(node,
                Has.Property(nameof(node.Value))
                    .TypeOf<StringValueNode>());

            var stringNode = (StringValueNode)node.Value!;
            Assert.That(stringNode,
                Has.Property(nameof(stringNode.Value))
                    .EqualTo("Breaking Bad"));
        }
    }

    [Test]
    public void ScalarExpr_ReturnsScalarNode_WhenValueIsNumber()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("fileSize = 1000000");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field)).EqualTo("fileSize"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.Equals));
            Assert.That(node,
                Has.Property(nameof(node.Value))
                    .TypeOf<NumberValueNode>());
        }
    }

    [Test]
    public void ScalarExpr_ReturnsScalarNode_WhenValueIsIdentifier()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("monitoring = Monitored");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field))
                    .EqualTo("monitoring"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.Equals));
            Assert.That(node,
                Has.Property(nameof(node.Value))
                    .TypeOf<IdentifierValueNode>());

            var identNode = (IdentifierValueNode)node.Value!;
            Assert.That(identNode,
                Has.Property(nameof(identNode.Value))
                    .EqualTo("Monitored"));
        }
    }

    [Test]
    public void ScalarExpr_ReturnsNotEquals_WhenOperatorIsNotEquals()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("status != \"active\"");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.NotEquals));
    }

    [Test]
    public void ScalarExpr_ReturnsGreaterThan_WhenOperatorIsGreaterThan()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("fileSize > 1000000000");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field)).EqualTo("fileSize"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.GreaterThan));
        }
    }

    [Test]
    public void ScalarExpr_ReturnsGreaterThanOrEqual_WhenOperatorIsGte()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("year >= 2020");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.GreaterThanOrEqual));
    }

    [Test]
    public void ScalarExpr_ReturnsLessThan_WhenOperatorIsLessThan()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("rating < 5");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.LessThan));
    }

    [Test]
    public void ScalarExpr_ReturnsLessThanOrEqual_WhenOperatorIsLte()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("duration <= \"02:00:00\"");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.LessThanOrEqual));
    }

    [Test]
    public void ScalarExpr_ReturnsContains_WhenOperatorIsContains()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("title contains \"Star\"");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field)).EqualTo("title"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.Contains));
        }
    }

    [Test]
    public void ScalarExpr_ReturnsNotContains_WhenOperatorIsNotContains()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("title !contains \"bad\"");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.NotContains));
    }

    [Test]
    public void ScalarExpr_ReturnsStartsWith_WhenOperatorIsStartsWith()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("name startswith \"The\"");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.StartsWith));
    }

    [Test]
    public void ScalarExpr_ReturnsNotStartsWith_WhenOperatorIsNotStartsWith()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("name !startswith \"A\"");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.NotStartsWith));
    }

    [Test]
    public void ScalarExpr_ReturnsEndsWith_WhenOperatorIsEndsWith()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("path endswith \".mp4\"");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.EndsWith));
    }

    [Test]
    public void ScalarExpr_ReturnsNotEndsWith_WhenOperatorIsNotEndsWith()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("path !endswith \".txt\"");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.NotEndsWith));
    }

    [Test]
    public void ScalarExpr_ReturnsListValue_WhenOperatorIsIn()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("status in (Active, Ended, Cancelled)");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field)).EqualTo("status"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.In));
            Assert.That(node,
                Has.Property(nameof(node.Value))
                    .TypeOf<ListValueNode>());

            var listNode = (ListValueNode)node.Value!;
            Assert.That(listNode,
                Has.Property(nameof(listNode.Values))
                    .Exactly(3).Items);
        }
    }

    [Test]
    public void ScalarExpr_ReturnsListValue_WhenOperatorIsNotIn()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("contentType !in (\"movie\", \"show\")");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.NotIn));
            Assert.That(node,
                Has.Property(nameof(node.Value))
                    .TypeOf<ListValueNode>());

            var listNode = (ListValueNode)node.Value!;
            Assert.That(listNode,
                Has.Property(nameof(listNode.Values))
                    .Exactly(2).Items);
        }
    }

    [Test]
    public void ScalarExpr_ReturnsNullValue_WhenOperatorIsNull()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("overview null");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field)).EqualTo("overview"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.IsNull));
            Assert.That(node,
                Has.Property(nameof(node.Value)).Null);
        }
    }

    [Test]
    public void ScalarExpr_ReturnsNullValue_WhenOperatorIsNotNull()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("publishDate !null");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field))
                    .EqualTo("publishDate"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.IsNotNull));
            Assert.That(node,
                Has.Property(nameof(node.Value)).Null);
        }
    }

    [Test]
    public void ScalarExpr_TracksPosition_WhenInputIsParsed()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("name = \"test\"");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Position)).EqualTo(0));
            Assert.That(node,
                Has.Property(nameof(node.Length))
                    .EqualTo(13)); // "name = \"test\""
        }
    }

    [Test]
    public void ScalarExpr_TracksPosition_WhenOperatorIsNull()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("field null");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Position)).EqualTo(0));
            Assert.That(node,
                Has.Property(nameof(node.Length))
                    .EqualTo(10)); // "field null"
        }
    }

    [Test]
    public void ScalarExpr_TracksPosition_WhenOperatorIsNotNull()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("field !null");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Position)).EqualTo(0));
            Assert.That(node,
                Has.Property(nameof(node.Length))
                    .EqualTo(11)); // "field !null"
        }
    }

    [Test]
    public void ScalarExpr_TracksPosition_WhenOperatorIsIn()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("status in (A, B)");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ScalarExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Position)).EqualTo(0));
            Assert.That(node,
                Has.Property(nameof(node.Length))
                    .EqualTo(16)); // "status in (A, B)"
        }
    }

    [Test]
    public void ScalarExpr_ReturnsError_WhenMissingOperator()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("name \"test\"");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).False);
    }

    [Test]
    public void ScalarExpr_ReturnsError_WhenMissingValue()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("name =");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).False);
    }

    [Test]
    public void ScalarExpr_ReturnsError_WhenInMissingValueList()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("status in");

        // Act
        var result = FilterQueryParsers.ScalarExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).False);
    }

    [Test]
    public void ItemValue_ReturnsItemNode_WhenInputIsString()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("\"Action\"");

        // Act
        var result = FilterQueryParsers.ItemValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value)).TypeOf<ItemValueNode>());

        var node = (ItemValueNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Value)).TypeOf<StringValueNode>());

        var stringNode = (StringValueNode)node.Value;
        Assert.That(stringNode,
            Has.Property(nameof(stringNode.Value)).EqualTo("Action"));
    }

    [Test]
    public void ItemValue_ReturnsItemNode_WhenInputIsIdentifier()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("true");

        // Act
        var result = FilterQueryParsers.ItemValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value)).TypeOf<ItemValueNode>());

        var node = (ItemValueNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Value))
                .TypeOf<IdentifierValueNode>());
    }

    [Test]
    public void ItemValue_TracksPosition_WhenInputIsParsed()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("\"test\"");

        // Act
        var result = FilterQueryParsers.ItemValue.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Position)).EqualTo(0));
            Assert.That(node,
                Has.Property(nameof(node.Length)).EqualTo(6));
        }
    }

    [Test]
    public void ItemField_ReturnsFieldNode_WhenOperatorIsEquals()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("name = \"Action\"");

        // Act
        var result = FilterQueryParsers.ItemField.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value))
                .TypeOf<ItemFieldNode>());

        var node = (ItemFieldNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field)).EqualTo("name"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.Equals));
            Assert.That(node,
                Has.Property(nameof(node.Value))
                    .TypeOf<StringValueNode>());

            var stringNode = (StringValueNode)node.Value;
            Assert.That(stringNode,
                Has.Property(nameof(stringNode.Value))
                    .EqualTo("Action"));
        }
    }

    [Test]
    public void ItemField_ReturnsFieldNode_WhenOperatorIsGte()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("year >= 2000");

        // Act
        var result = FilterQueryParsers.ItemField.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ItemFieldNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field)).EqualTo("year"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.GreaterThanOrEqual));
            Assert.That(node,
                Has.Property(nameof(node.Value))
                    .TypeOf<NumberValueNode>());
        }
    }

    [Test]
    public void ItemField_ReturnsFieldNode_WhenOperatorIsStartsWith()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("name startswith \"A\"");

        // Act
        var result = FilterQueryParsers.ItemField.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (ItemFieldNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.StartsWith));
    }

    [Test]
    public void ItemField_TracksPosition_WhenInputIsParsed()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("name = \"test\"");

        // Act
        var result = FilterQueryParsers.ItemField.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Position)).EqualTo(0));
            Assert.That(node,
                Has.Property(nameof(node.Length)).EqualTo(13));
        }
    }

    [Test]
    public void ItemTerm_ReturnsFieldNode_WhenInputIsFieldComparison()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("body = \"Academy Awards\"");

        // Act
        var result = FilterQueryParsers.ItemTerm.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value)).TypeOf<ItemFieldNode>());
    }

    [Test]
    public void ItemTerm_ReturnsValueNode_WhenInputIsSimpleValue()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("\"Action\"");

        // Act
        var result = FilterQueryParsers.ItemTerm.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value)).TypeOf<ItemValueNode>());
    }

    [Test]
    public void ItemTerm_ReturnsGroupNode_WhenInputIsGrouped()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("(name = \"Action\")");

        // Act
        var result = FilterQueryParsers.ItemTerm.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value))
                .TypeOf<ItemGroupNode>());

        var node = (ItemGroupNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Inner)).TypeOf<ItemFieldNode>());
    }

    [Test]
    public void ItemAndExpr_ReturnsFieldNode_WhenInputIsSingleTerm()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("name = \"Action\"");

        // Act
        var result = FilterQueryParsers.ItemAndExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value)).TypeOf<ItemFieldNode>());
    }

    [Test]
    public void ItemAndExpr_ReturnsBinaryNode_WhenInputIsAndChain()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize(
            "body = \"Academy Awards\" and name = \"Best Picture\"");

        // Act
        var result = FilterQueryParsers.ItemAndExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value))
                .TypeOf<ItemBinaryNode>());

        var node = (ItemBinaryNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(BinaryOperator.And));
            Assert.That(node,
                Has.Property(nameof(node.Left))
                    .TypeOf<ItemFieldNode>());
            Assert.That(node,
                Has.Property(nameof(node.Right))
                    .TypeOf<ItemFieldNode>());
        }
    }

    [Test]
    public void ItemAndExpr_ReturnsNestedBinaryNode_WhenInputHasMultipleAnds()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize(
            "body = \"A\" and name = \"B\" and year >= 2000");

        // Act
        var result = FilterQueryParsers.ItemAndExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value))
                .TypeOf<ItemBinaryNode>());

        var node = (ItemBinaryNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(BinaryOperator.And));
        Assert.That(node,
            Has.Property(nameof(node.Left))
                .TypeOf<ItemBinaryNode>());
    }

    [Test]
    public void ItemOrExpr_ReturnsFieldNode_WhenInputIsSingleTerm()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("name = \"Action\"");

        // Act
        var result = FilterQueryParsers.ItemOrExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value)).TypeOf<ItemFieldNode>());
    }

    [Test]
    public void ItemOrExpr_ReturnsBinaryNode_WhenInputIsOrChain()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize(
            "name = \"Oscar\" or name = \"Emmy\"");

        // Act
        var result = FilterQueryParsers.ItemOrExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value))
                .TypeOf<ItemBinaryNode>());

        var node = (ItemBinaryNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(BinaryOperator.Or));
            Assert.That(node,
                Has.Property(nameof(node.Left))
                    .TypeOf<ItemFieldNode>());
            Assert.That(node,
                Has.Property(nameof(node.Right))
                    .TypeOf<ItemFieldNode>());
        }
    }

    [Test]
    public void ItemExpr_ReturnsBinaryNode_WhenInputIsComplex()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize(
            "name = \"Oscar\" or (name = \"Emmy\" and year >= 2010)");

        // Act
        var result = FilterQueryParsers.ItemExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value))
                .TypeOf<ItemBinaryNode>());

        var node = (ItemBinaryNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(BinaryOperator.Or));
        Assert.That(node,
            Has.Property(nameof(node.Left)).TypeOf<ItemFieldNode>());
        Assert.That(node,
            Has.Property(nameof(node.Right))
                .TypeOf<ItemGroupNode>());
    }

    [Test]
    public void ItemExpr_TracksPosition_WhenInputIsParsed()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("name = \"A\" and year >= 2000");

        // Act
        var result = FilterQueryParsers.ItemExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Position)).EqualTo(0));
            Assert.That(node,
                Has.Property(nameof(node.Length)).EqualTo(27));
        }
    }

    [Test]
    public void CollectionExpr_ReturnsCollectionNode_WhenUsingHas()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("genre has \"Action\"");

        // Act
        var result = FilterQueryParsers.CollectionExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);
        Assert.That(result,
            Has.Property(nameof(result.Value))
                .TypeOf<CollectionExprNode>());

        var node = (CollectionExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field)).EqualTo("genre"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(CollectionOperator.Has));
            Assert.That(node,
                Has.Property(nameof(node.Item))
                    .TypeOf<ItemValueNode>());
        }
    }

    [Test]
    public void CollectionExpr_ReturnsCollectionNode_WhenUsingNotHas()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("genre !has \"Horror\"");

        // Act
        var result = FilterQueryParsers.CollectionExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (CollectionExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field)).EqualTo("genre"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(CollectionOperator.NotHas));
        }
    }

    [Test]
    public void CollectionExpr_ReturnsBinaryItem_WhenUsingComplexHas()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize(
            "awards has (body = \"Academy Awards\" and won = true)");

        // Act
        var result = FilterQueryParsers.CollectionExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (CollectionExprNode)result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field)).EqualTo("awards"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(CollectionOperator.Has));
            Assert.That(node,
                Has.Property(nameof(node.Item))
                    .TypeOf<ItemBinaryNode>());
        }
    }

    [Test]
    public void CollectionExpr_ReturnsOrNode_WhenUsingComplexWithOr()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize(
            "awards has (name = \"Oscar\" or (name = \"Emmy\" and year >= 2010))");

        // Act
        var result = FilterQueryParsers.CollectionExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = (CollectionExprNode)result.Value;
        Assert.That(node,
            Has.Property(nameof(node.Field)).EqualTo("awards"));
        var orNode = (ItemBinaryNode)node.Item;
        Assert.That(orNode,
            Has.Property(nameof(orNode.Operator))
                .EqualTo(BinaryOperator.Or));
    }

    [Test]
    public void CollectionExpr_TracksPosition_WhenInputIsSimple()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("genre has \"Action\"");

        // Act
        var result = FilterQueryParsers.CollectionExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Position)).EqualTo(0));
            Assert.That(node,
                Has.Property(nameof(node.Length)).EqualTo(18));
        }
    }

    [Test]
    public void CollectionExpr_TracksPosition_WhenInputIsComplex()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("awards has (body = \"X\")");

        // Act
        var result = FilterQueryParsers.CollectionExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).True);

        var node = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Position)).EqualTo(0));
            // Length is based on the inner item expression position/length (unwrapped)
            Assert.That(node,
                Has.Property(nameof(node.Length)).EqualTo(22));
        }
    }

    [Test]
    public void CollectionExpr_ReturnsError_WhenMissingValue()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("genre has");

        // Act
        var result = FilterQueryParsers.CollectionExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).False);
    }

    [Test]
    public void CollectionExpr_ReturnsError_WhenMissingCloseParen()
    {
        // Arrange
        var tokens = FilterQueryTokenizer.Tokenize("awards has (body = \"X\"");

        // Act
        var result = FilterQueryParsers.CollectionExpr.TryParse(tokens);

        // Assert
        Assert.That(result, Has.Property(nameof(result.HasValue)).False);
    }
}
