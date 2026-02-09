using Magpie.QueryLanguage;

using NUnit.Framework;

namespace Magpie.Test.QueryLanguage;

[TestFixture]
public class FilterQueryParserTests
{
    [Test]
    public void Parse_ReturnsError_WhenQueryEmpty()
    {
        // Act
        var result = FilterQueryParser.Parse(string.Empty);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.IsSuccess)).False);
            Assert.That(result, Has.Property(nameof(result.Error)).Not.Null);
        }

        var error = result.Error!.Value;
        Assert.That(error,
            Has.Property(nameof(error.Message)).EqualTo("Empty query"));
    }

    [Test]
    public void Parse_ReturnsError_WhenQueryWhitespaceOnly()
    {
        // Act
        var result = FilterQueryParser.Parse("   ");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).False);
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenSimpleEquals()
    {
        // Act
        var result = FilterQueryParser.Parse("name = \"Breaking Bad\"");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
            Assert.That(result,
                Has.Property(nameof(result.Value)).TypeOf<ScalarExprNode>());
        }

        var node = (ScalarExprNode)result.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node, Has.Property(nameof(node.Field)).EqualTo("name"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.Equals));
            Assert.That(node,
                Has.Property(nameof(node.Value)).TypeOf<StringValueNode>());
        }

        var value = (StringValueNode)node.Value!;
        Assert.That(value,
            Has.Property(nameof(value.Value)).EqualTo("Breaking Bad"));
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenEqualsNumber()
    {
        // Act
        var result = FilterQueryParser.Parse("fileSize > 1000000000");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
            Assert.That(result,
                Has.Property(nameof(result.Value)).TypeOf<ScalarExprNode>());
        }

        var node = (ScalarExprNode)result.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field)).EqualTo("fileSize"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.GreaterThan));
            Assert.That(node,
                Has.Property(nameof(node.Value)).TypeOf<NumberValueNode>());
        }

        var value = (NumberValueNode)node.Value!;
        Assert.That(value,
            Has.Property(nameof(value.Value)).EqualTo("1000000000"));
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenEqualsIdentifier()
    {
        // Act
        var result = FilterQueryParser.Parse("monitoring = Monitored");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
            Assert.That(result,
                Has.Property(nameof(result.Value))
                    .TypeOf<ScalarExprNode>());
        }

        var node = (ScalarExprNode)result.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field)).EqualTo("monitoring"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.Equals));
            Assert.That(node,
                Has.Property(nameof(node.Value))
                    .TypeOf<IdentifierValueNode>());
        }

        var value = (IdentifierValueNode)node.Value!;
        Assert.That(value,
            Has.Property(nameof(value.Value)).EqualTo("Monitored"));
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenNotEquals()
    {
        // Act
        var result = FilterQueryParser.Parse("status != \"active\"");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (ScalarExprNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.NotEquals));
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenGreaterThanOrEqual()
    {
        // Act
        var result = FilterQueryParser.Parse("year >= 2020");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (ScalarExprNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.GreaterThanOrEqual));
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenLessThan()
    {
        // Act
        var result = FilterQueryParser.Parse("duration < \"02:00:00\"");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (ScalarExprNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.LessThan));
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenLessThanOrEqual()
    {
        // Act
        var result = FilterQueryParser.Parse("count <= 10");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (ScalarExprNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.LessThanOrEqual));
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenContains()
    {
        // Act
        var result = FilterQueryParser.Parse("title contains \"Star\"");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (ScalarExprNode)result.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.Contains));
            Assert.That(node,
                Has.Property(nameof(node.Field)).EqualTo("title"));
        }
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenNotContains()
    {
        // Act
        var result = FilterQueryParser.Parse("title !contains \"bad\"");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (ScalarExprNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.NotContains));
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenStartsWith()
    {
        // Act
        var result = FilterQueryParser.Parse("name startswith \"The\"");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (ScalarExprNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.StartsWith));
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenNotStartsWith()
    {
        // Act
        var result = FilterQueryParser.Parse("name !startswith \"A\"");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (ScalarExprNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.NotStartsWith));
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenEndsWith()
    {
        // Act
        var result = FilterQueryParser.Parse("name endswith \"ion\"");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (ScalarExprNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.EndsWith));
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenNotEndsWith()
    {
        // Act
        var result = FilterQueryParser.Parse("name !endswith \"s\"");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (ScalarExprNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(ScalarOperator.NotEndsWith));
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenNull()
    {
        // Act
        var result = FilterQueryParser.Parse("overview null");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (ScalarExprNode)result.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.IsNull));
            Assert.That(node, Has.Property(nameof(node.Value)).Null);
        }
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenNotNull()
    {
        // Act
        var result = FilterQueryParser.Parse("overview !null");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (ScalarExprNode)result.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.IsNotNull));
            Assert.That(node, Has.Property(nameof(node.Value)).Null);
        }
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenIn()
    {
        // Act
        var result = FilterQueryParser.Parse(
            "contentType in (\"movie\", \"show\")");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (ScalarExprNode)result.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.In));
            Assert.That(node,
                Has.Property(nameof(node.Value)).TypeOf<ListValueNode>());
        }

        var list = (ListValueNode)node.Value!;
        Assert.That(list,
            Has.Property(nameof(list.Values)).Exactly(2).Items);
    }

    [Test]
    public void Parse_ReturnsScalarExprNode_WhenNotIn()
    {
        // Act
        var result = FilterQueryParser.Parse("status !in (Active, Ended)");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (ScalarExprNode)result.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(ScalarOperator.NotIn));
            Assert.That(node,
                Has.Property(nameof(node.Value)).TypeOf<ListValueNode>());
        }
    }

    [Test]
    public void Parse_ReturnsBinaryNode_WhenAnd()
    {
        // Act
        var result = FilterQueryParser.Parse(
            "monitoring = Monitored and contentType = \"show\"");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
            Assert.That(result,
                Has.Property(nameof(result.Value)).TypeOf<BinaryNode>());
        }

        var node = (BinaryNode)result.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(BinaryOperator.And));
            Assert.That(node,
                Has.Property(nameof(node.Left)).TypeOf<ScalarExprNode>());
            Assert.That(node,
                Has.Property(nameof(node.Right)).TypeOf<ScalarExprNode>());
        }
    }

    [Test]
    public void Parse_ReturnsBinaryNode_WhenOr()
    {
        // Act
        var result = FilterQueryParser.Parse(
            "genre has \"Action\" or genre has \"Comedy\"");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
            Assert.That(result,
                Has.Property(nameof(result.Value)).TypeOf<BinaryNode>());
        }

        var node = (BinaryNode)result.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(BinaryOperator.Or));
            Assert.That(node,
                Has.Property(nameof(node.Left))
                    .TypeOf<CollectionExprNode>());
            Assert.That(node,
                Has.Property(nameof(node.Right))
                    .TypeOf<CollectionExprNode>());
        }
    }

    [Test]
    public void Parse_ReturnsNotNode_WhenNot()
    {
        // Act
        var result = FilterQueryParser.Parse("not genre has \"Horror\"");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
            Assert.That(result,
                Has.Property(nameof(result.Value)).TypeOf<NotNode>());
        }

        var node = (NotNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Operand))
                .TypeOf<CollectionExprNode>());
    }

    [Test]
    public void Parse_ReturnsGroupNode_WhenParenthesized()
    {
        // Act
        var result = FilterQueryParser.Parse("(name = \"test\")");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
            Assert.That(result,
                Has.Property(nameof(result.Value)).TypeOf<GroupNode>());
        }

        var node = (GroupNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Inner)).TypeOf<ScalarExprNode>());
    }

    [Test]
    public void Parse_BindsAndBeforeOr_WhenMixedPrecedence()
    {
        // Act
        var result = FilterQueryParser.Parse("a = 1 or b = 2 and c = 3");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (BinaryNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(BinaryOperator.Or));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Left)).TypeOf<ScalarExprNode>());
            Assert.That(node,
                Has.Property(nameof(node.Right)).TypeOf<BinaryNode>());
        }

        var rightAnd = (BinaryNode)node.Right;
        Assert.That(rightAnd,
            Has.Property(nameof(rightAnd.Operator))
                .EqualTo(BinaryOperator.And));
    }

    [Test]
    public void Parse_OverridesPrecedence_WhenParenthesizedOr()
    {
        // Act
        var result = FilterQueryParser.Parse("(a = 1 or b = 2) and c = 3");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (BinaryNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(BinaryOperator.And));
        Assert.That(node,
            Has.Property(nameof(node.Left)).TypeOf<GroupNode>());
    }

    [Test]
    public void Parse_ReturnsCollectionExprNode_WhenSimpleHas()
    {
        // Act
        var result = FilterQueryParser.Parse("genre has \"Action\"");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
            Assert.That(result,
                Has.Property(nameof(result.Value))
                    .TypeOf<CollectionExprNode>());
        }

        var node = (CollectionExprNode)result.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Field)).EqualTo("genre"));
            Assert.That(node,
                Has.Property(nameof(node.Operator))
                    .EqualTo(CollectionOperator.Has));
            Assert.That(node,
                Has.Property(nameof(node.Item)).TypeOf<ItemValueNode>());
        }

        var item = (ItemValueNode)node.Item;
        Assert.That(item,
            Has.Property(nameof(item.Value)).TypeOf<StringValueNode>());
    }

    [Test]
    public void Parse_ReturnsCollectionExprNode_WhenSimpleNotHas()
    {
        // Act
        var result = FilterQueryParser.Parse("genre !has \"Horror\"");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
            Assert.That(result,
                Has.Property(nameof(result.Value))
                    .TypeOf<CollectionExprNode>());
        }

        var node = (CollectionExprNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Operator))
                .EqualTo(CollectionOperator.NotHas));
    }

    [Test]
    public void Parse_ReturnsCollectionExprNode_WhenComplexItemExpression()
    {
        // Act
        var result = FilterQueryParser.Parse(
            "genres has (name startswith \"A\" and name endswith \"ion\")");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
            Assert.That(result,
                Has.Property(nameof(result.Value))
                    .TypeOf<CollectionExprNode>());
        }

        var node = (CollectionExprNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Item)).TypeOf<ItemBinaryNode>());

        var itemBinary = (ItemBinaryNode)node.Item;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(itemBinary,
                Has.Property(nameof(itemBinary.Operator))
                    .EqualTo(BinaryOperator.And));
            Assert.That(itemBinary,
                Has.Property(nameof(itemBinary.Left))
                    .TypeOf<ItemFieldNode>());
            Assert.That(itemBinary,
                Has.Property(nameof(itemBinary.Right))
                    .TypeOf<ItemFieldNode>());
        }
    }

    [Test]
    public void Parse_ReturnsCollectionExprNode_WhenComplexAwardExpression()
    {
        // Act
        var result = FilterQueryParser.Parse(
            "awards has (body = \"Academy Awards\""
            + " and name = \"Best Picture\" and year >= 2000)");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
            Assert.That(result,
                Has.Property(nameof(result.Value))
                    .TypeOf<CollectionExprNode>());
        }

        var node = (CollectionExprNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Item)).TypeOf<ItemBinaryNode>());
    }

    [Test]
    public void Parse_ReturnsCollectionExprNode_WhenItemExpressionWithOr()
    {
        // Act
        var result = FilterQueryParser.Parse(
            "awards has (name = \"Oscar\""
            + " or (name = \"Emmy\" and year >= 2010))");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (CollectionExprNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Item)).TypeOf<ItemBinaryNode>());

        var itemBinary = (ItemBinaryNode)node.Item;
        Assert.That(itemBinary,
            Has.Property(nameof(itemBinary.Operator))
                .EqualTo(BinaryOperator.Or));
    }

    [Test]
    public void Parse_ReturnsCollectionExprNode_WhenSimpleValueInItem()
    {
        // Act
        var result = FilterQueryParser.Parse("genre has (\"Action\")");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
            Assert.That(result,
                Has.Property(nameof(result.Value))
                    .TypeOf<CollectionExprNode>());
        }

        var node = (CollectionExprNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Item)).TypeOf<ItemValueNode>());
    }

    [Test]
    public void Parse_ReturnsAst_WhenQueryIsComplex()
    {
        // Act
        var result = FilterQueryParser.Parse(
            "monitoring = Monitored and contentType = \"movie\""
            + " and genre has \"Action\""
            + " and genre !has \"Horror\" and fileSize > 1000000000");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var root = (BinaryNode)result.Value!;
        Assert.That(root,
            Has.Property(nameof(root.Operator))
                .EqualTo(BinaryOperator.And));
    }

    [Test]
    public void Parse_TracksPosition_WhenSimpleExpression()
    {
        // Act
        var result = FilterQueryParser.Parse("name = \"test\"");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (ScalarExprNode)result.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Position)).EqualTo(0));
            Assert.That(node,
                Has.Property(nameof(node.Length)).EqualTo(13));
        }
    }

    [Test]
    public void Parse_TracksPosition_WhenBinaryExpression()
    {
        // Act
        var result = FilterQueryParser.Parse("a = 1 and b = 2");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (BinaryNode)result.Value!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node,
                Has.Property(nameof(node.Position)).EqualTo(0));
            Assert.That(node,
                Has.Property(nameof(node.Length)).EqualTo(15));
        }
    }

    [Test]
    public void Parse_ReturnsError_WhenMissingOperator()
    {
        // Act
        var result = FilterQueryParser.Parse("name \"test\"");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result,
                Has.Property(nameof(result.IsSuccess)).False);
            var error = result.Error!.Value;
            Assert.That(error,
                Has.Property(nameof(error.Position)).EqualTo(5));
        }
    }

    [Test]
    public void Parse_ReturnsError_WhenMissingValue()
    {
        // Act
        var result = FilterQueryParser.Parse("name =");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result,
                Has.Property(nameof(result.IsSuccess)).False);
            var error = result.Error!.Value;
            Assert.That(error,
                Has.Property(nameof(error.Expected))
                    .SupersetOf(new[] { "string", "number", "identifier" }));
        }
    }

    [Test]
    public void Parse_ReturnsError_WhenMissingRightParen()
    {
        // Act
        var result = FilterQueryParser.Parse("(name = \"test\"");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result,
                Has.Property(nameof(result.IsSuccess)).False);
            var error = result.Error!.Value;
            Assert.That(error,
                Has.Property(nameof(error.Message))
                    .EqualTo("Expected ')'"));
        }
    }

    [Test]
    public void Parse_ReturnsError_WhenUnexpectedTokenAfterExpression()
    {
        // Act
        var result = FilterQueryParser.Parse("name = \"test\" extra");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result,
                Has.Property(nameof(result.IsSuccess)).False);
            var error = result.Error!.Value;
            Assert.That(error,
                Has.Property(nameof(error.Message))
                    .Contains("Unexpected token"));
        }
    }

    [Test]
    public void Parse_ReturnsError_WhenEmptyInList()
    {
        // Act
        var result = FilterQueryParser.Parse("type in ()");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result,
                Has.Property(nameof(result.IsSuccess)).False);
            var error = result.Error!.Value;
            Assert.That(error,
                Has.Property(nameof(error.Message))
                    .EqualTo("Empty value list"));
        }
    }

    [Test]
    public void Parse_ReturnsError_WhenLexerError()
    {
        // Act
        var result = FilterQueryParser.Parse("name @ \"test\"");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).False);
    }

    [Test]
    public void Parse_Succeeds_WhenKeywordsAreMixedCase()
    {
        // Act
        var result = FilterQueryParser.Parse(
            "name = \"a\" AND type = \"b\" OR status = \"c\"");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
    }

    [Test]
    public void Parse_ReturnsError_WhenMissingFieldName()
    {
        // Act
        var result = FilterQueryParser.Parse("= \"test\"");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result,
                Has.Property(nameof(result.IsSuccess)).False);
            var error = result.Error!.Value;
            Assert.That(error,
                Has.Property(nameof(error.Message))
                    .Contains("Expected field name"));
        }
    }

    [Test]
    public void Parse_ReturnsNestedBinary_WhenMultipleAndsUsed()
    {
        // Act
        var result = FilterQueryParser.Parse(
            "a = 1 and b = 2 and c = 3 and d = 4");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
    }

    [Test]
    public void Parse_ReturnsNestedBinary_WhenMultipleOrsUsed()
    {
        // Act
        var result = FilterQueryParser.Parse(
            "a = 1 or b = 2 or c = 3 or d = 4");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
    }

    [Test]
    public void Parse_ReturnsNestedGroupNode_WhenDoubleParenthesized()
    {
        // Act
        var result = FilterQueryParser.Parse("((a = 1))");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
            Assert.That(result,
                Has.Property(nameof(result.Value)).TypeOf<GroupNode>());
        }

        var outer = (GroupNode)result.Value!;
        Assert.That(outer,
            Has.Property(nameof(outer.Inner)).TypeOf<GroupNode>());
    }

    [Test]
    public void Parse_ReturnsNotNode_WhenNotPrecedesGroup()
    {
        // Act
        var result = FilterQueryParser.Parse("not (a = 1 or b = 2)");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var notNode = (NotNode)result.Value!;
        Assert.That(notNode,
            Has.Property(nameof(notNode.Operand)).TypeOf<GroupNode>());
    }

    [Test]
    public void Parse_ReturnsItemGroupNode_WhenCollectionHasGroupedItem()
    {
        // Act
        var result = FilterQueryParser.Parse(
            "awards has ((name = \"Oscar\"))");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var node = (CollectionExprNode)result.Value!;
        Assert.That(node,
            Has.Property(nameof(node.Item)).TypeOf<ItemGroupNode>());
    }

    [Test]
    public void Parse_ReturnsError_WhenMissingRightParenInCollection()
    {
        // Act
        var result = FilterQueryParser.Parse(
            "genres has (name = \"Action\"");

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).False);
    }

    [Test]
    public void Parse_ReturnsError_WhenMissingRightParenInValueList()
    {
        // Act
        var result = FilterQueryParser.Parse("type in (\"a\", \"b\"");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result,
                Has.Property(nameof(result.IsSuccess)).False);
            var error = result.Error!.Value;
            Assert.That(error,
                Has.Property(nameof(error.Message))
                    .Contains("Expected ')'"));
        }
    }

    [Test]
    public void ParseResult_Success_ReturnsIsSuccessTrue()
    {
        // Act
        var result = ParseResult<QueryNode>.Success(
            new ScalarExprNode("test", ScalarOperator.Equals, null));

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);
    }

    [Test]
    public void ParseResult_Failure_ReturnsIsSuccessFalse()
    {
        // Act
        var result = ParseResult<QueryNode>.Failure(
            new ParseError("error", 0, []));

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).False);
    }
}
