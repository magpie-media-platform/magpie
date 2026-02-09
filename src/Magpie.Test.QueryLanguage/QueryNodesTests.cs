using System.Collections.Generic;

using Magpie.QueryLanguage;

using NUnit.Framework;

namespace Magpie.Test.QueryLanguage;

[TestFixture]
public class QueryNodesTests
{
    [Test]
    public void BinaryNode_StoresProperties_WhenConstructed()
    {
        // Arrange
        var left = new ScalarExprNode("name", ScalarOperator.Equals, new StringValueNode("test"))
        {
            Position = 0,
            Length = 12,
        };
        var right = new ScalarExprNode("age", ScalarOperator.GreaterThan, new NumberValueNode("18"))
        {
            Position = 17,
            Length = 8,
        };

        // Act
        var node = new BinaryNode(BinaryOperator.And, left, right)
        {
            Position = 0,
            Length = 25,
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node, Has.Property(nameof(BinaryNode.Operator)).EqualTo(BinaryOperator.And));
            Assert.That(node, Has.Property(nameof(BinaryNode.Left)).EqualTo(left));
            Assert.That(node, Has.Property(nameof(BinaryNode.Right)).EqualTo(right));
            Assert.That(node, Has.Property(nameof(BinaryNode.Position)).EqualTo(0));
            Assert.That(node, Has.Property(nameof(BinaryNode.Length)).EqualTo(25));
        }
    }

    [Test]
    public void NotNode_StoresOperand_WhenConstructed()
    {
        // Arrange
        var operand = new ScalarExprNode("enabled", ScalarOperator.Equals, new IdentifierValueNode("true"))
        {
            Position = 4,
            Length = 14,
        };

        // Act
        var node = new NotNode(operand)
        {
            Position = 0,
            Length = 18,
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node, Has.Property(nameof(NotNode.Operand)).EqualTo(operand));
            Assert.That(node, Has.Property(nameof(NotNode.Position)).EqualTo(0));
            Assert.That(node, Has.Property(nameof(NotNode.Length)).EqualTo(18));
        }
    }

    [Test]
    public void GroupNode_StoresInnerExpression_WhenConstructed()
    {
        // Arrange
        var inner = new ScalarExprNode("status", ScalarOperator.NotEquals, new StringValueNode("archived"))
        {
            Position = 1,
            Length = 20,
        };

        // Act
        var node = new GroupNode(inner)
        {
            Position = 0,
            Length = 22,
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node, Has.Property(nameof(GroupNode.Inner)).EqualTo(inner));
            Assert.That(node, Has.Property(nameof(GroupNode.Position)).EqualTo(0));
            Assert.That(node, Has.Property(nameof(GroupNode.Length)).EqualTo(22));
        }
    }

    [Test]
    public void ScalarExprNode_StoresProperties_WhenConstructed()
    {
        // Arrange & Act
        var node = new ScalarExprNode("title", ScalarOperator.Contains, new StringValueNode("Star"))
        {
            Position = 0,
            Length = 20,
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node, Has.Property(nameof(ScalarExprNode.Field)).EqualTo("title"));
            Assert.That(node, Has.Property(nameof(ScalarExprNode.Operator)).EqualTo(ScalarOperator.Contains));
            Assert.That(node, Has.Property(nameof(ScalarExprNode.Value)).Not.Null);
        }
    }

    [Test]
    public void ScalarExprNode_AllowsNullValue_ForNullOperators()
    {
        // Arrange & Act
        var isNullNode = new ScalarExprNode("name", ScalarOperator.IsNull, null)
        {
            Position = 0,
            Length = 9,
        };
        var isNotNullNode = new ScalarExprNode("name", ScalarOperator.IsNotNull, null)
        {
            Position = 0,
            Length = 10,
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isNullNode, Has.Property(nameof(ScalarExprNode.Operator)).EqualTo(ScalarOperator.IsNull));
            Assert.That(isNullNode, Has.Property(nameof(ScalarExprNode.Value)).Null);
            Assert.That(isNotNullNode, Has.Property(nameof(ScalarExprNode.Operator))
                .EqualTo(ScalarOperator.IsNotNull));
            Assert.That(isNotNullNode, Has.Property(nameof(ScalarExprNode.Value)).Null);
        }
    }

    [Test]
    public void CollectionExprNode_StoresProperties_WhenConstructed()
    {
        // Arrange
        var item = new ItemValueNode(new StringValueNode("Action") { Position = 11, Length = 8 })
        {
            Position = 11,
            Length = 8,
        };

        // Act
        var node = new CollectionExprNode("genre", CollectionOperator.Has, item)
        {
            Position = 0,
            Length = 19,
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node, Has.Property(nameof(CollectionExprNode.Field)).EqualTo("genre"));
            Assert.That(node, Has.Property(nameof(CollectionExprNode.Operator)).EqualTo(CollectionOperator.Has));
            Assert.That(node, Has.Property(nameof(CollectionExprNode.Item)).EqualTo(item));
        }
    }

    [Test]
    public void CollectionExprNode_StoresOperator_WhenNotHas()
    {
        // Arrange
        var item = new ItemValueNode(new StringValueNode("Horror") { Position = 12, Length = 8 })
        {
            Position = 12,
            Length = 8,
        };

        // Act
        var node = new CollectionExprNode("genre", CollectionOperator.NotHas, item)
        {
            Position = 0,
            Length = 20,
        };

        // Assert
        Assert.That(node, Has.Property(nameof(CollectionExprNode.Operator)).EqualTo(CollectionOperator.NotHas));
    }

    [Test]
    public void ItemBinaryNode_StoresProperties_WhenConstructed()
    {
        // Arrange
        var left = new ItemFieldNode(
            "name",
            ScalarOperator.StartsWith,
            new StringValueNode("A") { Position = 1, Length = 3 })
        {
            Position = 1,
            Length = 17,
        };
        var right = new ItemFieldNode(
            "name",
            ScalarOperator.EndsWith,
            new StringValueNode("ion") { Position = 34, Length = 5 })
        {
            Position = 23,
            Length = 16,
        };

        // Act
        var node = new ItemBinaryNode(BinaryOperator.And, left, right)
        {
            Position = 1,
            Length = 38,
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node, Has.Property(nameof(ItemBinaryNode.Operator)).EqualTo(BinaryOperator.And));
            Assert.That(node, Has.Property(nameof(ItemBinaryNode.Left)).EqualTo(left));
            Assert.That(node, Has.Property(nameof(ItemBinaryNode.Right)).EqualTo(right));
        }
    }

    [Test]
    public void ItemGroupNode_StoresInnerExpression_WhenConstructed()
    {
        // Arrange
        var inner = new ItemFieldNode(
            "year",
            ScalarOperator.GreaterThanOrEqual,
            new NumberValueNode("2000") { Position = 8, Length = 4 })
        {
            Position = 1,
            Length = 11,
        };

        // Act
        var node = new ItemGroupNode(inner)
        {
            Position = 0,
            Length = 13,
        };

        // Assert
        Assert.That(node, Has.Property(nameof(ItemGroupNode.Inner)).EqualTo(inner));
    }

    [Test]
    public void ItemValueNode_StoresValue_WhenConstructed()
    {
        // Arrange
        var value = new StringValueNode("Comedy") { Position = 0, Length = 8 };

        // Act
        var node = new ItemValueNode(value)
        {
            Position = 0,
            Length = 8,
        };

        // Assert
        Assert.That(node, Has.Property(nameof(ItemValueNode.Value)).EqualTo(value));
    }

    [Test]
    public void ItemFieldNode_StoresProperties_WhenConstructed()
    {
        // Arrange
        var value = new StringValueNode("Academy Awards") { Position = 7, Length = 16 };

        // Act
        var node = new ItemFieldNode("body", ScalarOperator.Equals, value)
        {
            Position = 0,
            Length = 23,
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node, Has.Property(nameof(ItemFieldNode.Field)).EqualTo("body"));
            Assert.That(node, Has.Property(nameof(ItemFieldNode.Operator)).EqualTo(ScalarOperator.Equals));
            Assert.That(node, Has.Property(nameof(ItemFieldNode.Value)).EqualTo(value));
        }
    }

    [Test]
    public void StringValueNode_StoresValue_WhenConstructed()
    {
        // Arrange & Act
        var node = new StringValueNode("Breaking Bad")
        {
            Position = 7,
            Length = 14,
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node, Has.Property(nameof(StringValueNode.Value)).EqualTo("Breaking Bad"));
            Assert.That(node, Has.Property(nameof(StringValueNode.Position)).EqualTo(7));
            Assert.That(node, Has.Property(nameof(StringValueNode.Length)).EqualTo(14));
        }
    }

    [Test]
    public void NumberValueNode_StoresValue_WhenConstructed()
    {
        // Arrange & Act
        var node = new NumberValueNode("1000000000")
        {
            Position = 12,
            Length = 10,
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node, Has.Property(nameof(NumberValueNode.Value)).EqualTo("1000000000"));
            Assert.That(node, Has.Property(nameof(NumberValueNode.Position)).EqualTo(12));
            Assert.That(node, Has.Property(nameof(NumberValueNode.Length)).EqualTo(10));
        }
    }

    [Test]
    public void IdentifierValueNode_StoresValue_WhenConstructed()
    {
        // Arrange & Act
        var node = new IdentifierValueNode("Monitored")
        {
            Position = 14,
            Length = 9,
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node, Has.Property(nameof(IdentifierValueNode.Value)).EqualTo("Monitored"));
            Assert.That(node, Has.Property(nameof(IdentifierValueNode.Position)).EqualTo(14));
            Assert.That(node, Has.Property(nameof(IdentifierValueNode.Length)).EqualTo(9));
        }
    }

    [Test]
    public void ListValueNode_StoresValues_WhenConstructed()
    {
        // Arrange
        var values = new List<ValueNode>
        {
            new StringValueNode("movie") { Position = 16, Length = 7 },
            new StringValueNode("show") { Position = 25, Length = 6 },
        };

        // Act
        var node = new ListValueNode(values)
        {
            Position = 15,
            Length = 17,
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node, Has.Property(nameof(ListValueNode.Values)).With.Exactly(2).Items);
            Assert.That(node, Has.Property(nameof(ListValueNode.Position)).EqualTo(15));
            Assert.That(node, Has.Property(nameof(ListValueNode.Length)).EqualTo(17));
        }
    }

    [Test]
    public void ScalarOperator_ContainsAllValues_WhenEnumInspected()
    {
        // Assert
        Assert.That(typeof(ScalarOperator).GetEnumValues(), Has.Exactly(16).Items);
    }

    [Test]
    public void BinaryOperator_ContainsAllValues_WhenEnumInspected()
    {
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(typeof(BinaryOperator).GetEnumValues(), Has.Exactly(2).Items);
            Assert.That(BinaryOperator.And, Is.EqualTo((BinaryOperator)0));
            Assert.That(BinaryOperator.Or, Is.EqualTo((BinaryOperator)1));
        }
    }

    [Test]
    public void CollectionOperator_ContainsAllValues_WhenEnumInspected()
    {
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(typeof(CollectionOperator).GetEnumValues(), Has.Exactly(2).Items);
            Assert.That(CollectionOperator.Has, Is.EqualTo((CollectionOperator)0));
            Assert.That(CollectionOperator.NotHas, Is.EqualTo((CollectionOperator)1));
        }
    }
}
