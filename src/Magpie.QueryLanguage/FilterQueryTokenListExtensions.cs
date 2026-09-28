using System.Collections.Generic;
using System.Linq;
using System.Text;

using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace Magpie.QueryLanguage;

internal static class FilterQueryTokenListExtensions
{
    private static readonly (FilterQueryToken Token, ScalarOperator Op)[] ComparisonOps =
    [
        (FilterQueryToken.Equals, ScalarOperator.Equals),
        (FilterQueryToken.NotEquals, ScalarOperator.NotEquals),
        (FilterQueryToken.GreaterThanOrEqual, ScalarOperator.GreaterThanOrEqual),
        (FilterQueryToken.GreaterThan, ScalarOperator.GreaterThan),
        (FilterQueryToken.LessThanOrEqual, ScalarOperator.LessThanOrEqual),
        (FilterQueryToken.LessThan, ScalarOperator.LessThan),
        (FilterQueryToken.Contains, ScalarOperator.Contains),
        (FilterQueryToken.NotContains, ScalarOperator.NotContains),
        (FilterQueryToken.StartsWith, ScalarOperator.StartsWith),
        (FilterQueryToken.NotStartsWith, ScalarOperator.NotStartsWith),
        (FilterQueryToken.EndsWith, ScalarOperator.EndsWith),
        (FilterQueryToken.NotEndsWith, ScalarOperator.NotEndsWith),
    ];

    private static readonly (FilterQueryToken Token, ScalarOperator Op)[] NullOps =
    [
        (FilterQueryToken.Null, ScalarOperator.IsNull),
        (FilterQueryToken.NotNull, ScalarOperator.IsNotNull),
    ];

    private static readonly (FilterQueryToken Token, ScalarOperator Op)[] MembershipAndNullOps =
    [
        (FilterQueryToken.In, ScalarOperator.In),
        (FilterQueryToken.NotIn, ScalarOperator.NotIn),
        .. NullOps,
    ];

    private static TokenListParser<FilterQueryToken, (ScalarOperator Op, int End)> ScalarOpWithPosition { get; } =
        BuildScalarOpParser([.. ComparisonOps, .. MembershipAndNullOps]);

    private static TokenListParser<FilterQueryToken, (ScalarOperator Op, int End)> ItemScalarOpWithPosition { get; } =
        BuildScalarOpParser([.. ComparisonOps, .. MembershipAndNullOps]);

    private static TokenListParser<FilterQueryToken, ValueNode> StringValue { get; } =
        Token.EqualTo(FilterQueryToken.String)
            .Select(token =>
            {
                var span = token.Span.ToStringValue();
                var content = UnescapeString(span);
                return (ValueNode)new StringValueNode(content)
                {
                    Position = token.Span.Position.Absolute,
                    Length = token.Span.Length,
                };
            });

    private static TokenListParser<FilterQueryToken, ValueNode> NumberValue { get; } =
        Token.EqualTo(FilterQueryToken.Number)
            .Select(token => (ValueNode)new NumberValueNode(token.Span.ToStringValue())
            {
                Position = token.Span.Position.Absolute,
                Length = token.Span.Length,
            });

    private static TokenListParser<FilterQueryToken, ValueNode> IdentifierValue { get; } =
        Token.EqualTo(FilterQueryToken.Identifier)
            .Select(token => (ValueNode)new IdentifierValueNode(token.Span.ToStringValue())
            {
                Position = token.Span.Position.Absolute,
                Length = token.Span.Length,
            });

    private static TokenListParser<FilterQueryToken, ValueNode> Value { get; } =
        StringValue
            .Or(NumberValue)
            .Or(IdentifierValue);

    private static TokenListParser<FilterQueryToken, ValueNode> ValueList { get; } =
        from open in Token.EqualTo(FilterQueryToken.LeftParen)
        from first in Value
        from rest in Token.EqualTo(FilterQueryToken.Comma).IgnoreThen(Value).Many()
        from close in Token.EqualTo(FilterQueryToken.RightParen)
        select (ValueNode)new ListValueNode([first, .. rest])
        {
            Position = open.Span.Position.Absolute,
            Length = close.Span.Position.Absolute + close.Span.Length - open.Span.Position.Absolute,
        };

    private static TokenListParser<FilterQueryToken, CollectionOperator> CollectionOp { get; } =
        Token.EqualTo(FilterQueryToken.Has).Value(CollectionOperator.Has)
            .Or(Token.EqualTo(FilterQueryToken.NotHas).Value(CollectionOperator.NotHas));

    private static TokenListParser<FilterQueryToken, QueryNode> ScalarExpr { get; } =
        from field in Token.EqualTo(FilterQueryToken.Identifier)
        from opInfo in ScalarOpWithPosition
        from value in opInfo.Op is ScalarOperator.IsNull or ScalarOperator.IsNotNull
            ? Parse.Return<FilterQueryToken, ValueNode?>(null)
            : opInfo.Op is ScalarOperator.In or ScalarOperator.NotIn
                ? ValueList.Select(v => (ValueNode?)v)
                : Value.Select(v => (ValueNode?)v)
        select (QueryNode)new ScalarExprNode(field.Span.ToStringValue(), opInfo.Op, value)
        {
            Position = field.Span.Position.Absolute,
            Length = value is not null
                ? value.Position + value.Length - field.Span.Position.Absolute
                : opInfo.End - field.Span.Position.Absolute,
        };

    private static TokenListParser<FilterQueryToken, ItemExprNode> ItemValue { get; } =
        Value.Select(v => (ItemExprNode)new ItemValueNode(v)
        {
            Position = v.Position,
            Length = v.Length,
        });

    private static TokenListParser<FilterQueryToken, ItemExprNode> ItemField { get; } =
        from field in Token.EqualTo(FilterQueryToken.Identifier)
        from opInfo in ItemScalarOpWithPosition
        from value in opInfo.Op is ScalarOperator.IsNull or ScalarOperator.IsNotNull
            ? Parse.Return<FilterQueryToken, ValueNode?>(null)
            : opInfo.Op is ScalarOperator.In or ScalarOperator.NotIn
                ? ValueList.Select(v => (ValueNode?)v)
                : Value.Select(v => (ValueNode?)v)
        select (ItemExprNode)new ItemFieldNode(field.Span.ToStringValue(), opInfo.Op, value)
        {
            Position = field.Span.Position.Absolute,
            Length = value is not null
                ? value.Position + value.Length - field.Span.Position.Absolute
                : opInfo.End - field.Span.Position.Absolute,
        };

    private static TokenListParser<FilterQueryToken, ItemExprNode> ItemGroup { get; } =
        from open in Token.EqualTo(FilterQueryToken.LeftParen)
        from inner in Parse.Ref(() => ItemExpr!)
        from close in Token.EqualTo(FilterQueryToken.RightParen)
        select (ItemExprNode)new ItemGroupNode(inner)
        {
            Position = open.Span.Position.Absolute,
            Length = close.Span.Position.Absolute + close.Span.Length - open.Span.Position.Absolute,
        };

    private static TokenListParser<FilterQueryToken, ItemExprNode> ItemTerm { get; } =
        ItemField.Try()
            .Or(ItemGroup)
            .Or(ItemValue);

    private static TokenListParser<FilterQueryToken, ItemExprNode> ItemAndExpr { get; } =
        Parse.Chain(
            Token.EqualTo(FilterQueryToken.And),
            ItemTerm,
            (op, left, right) =>
            {
                var position = left.Position;
                var length = right.Position + right.Length - position;
                return new ItemBinaryNode(BinaryOperator.And, left, right)
                {
                    Position = position,
                    Length = length,
                };
            });

    private static TokenListParser<FilterQueryToken, ItemExprNode> ItemOrExpr { get; } =
        Parse.Chain(
            Token.EqualTo(FilterQueryToken.Or),
            ItemAndExpr,
            (op, left, right) =>
            {
                var position = left.Position;
                var length = right.Position + right.Length - position;
                return new ItemBinaryNode(BinaryOperator.Or, left, right)
                {
                    Position = position,
                    Length = length,
                };
            });

    private static TokenListParser<FilterQueryToken, ItemExprNode> ItemExpr { get; } = ItemOrExpr;

    private static TokenListParser<FilterQueryToken, ItemExprNode> CollectionItemOrValue { get; } = (
        from open in Token.EqualTo(FilterQueryToken.LeftParen)
        from expr in ItemExpr
        from close in Token.EqualTo(FilterQueryToken.RightParen)
        select expr).Try()
            .Or(ItemValue);

    private static TokenListParser<FilterQueryToken, QueryNode> CollectionExpr { get; } =
        from field in Token.EqualTo(FilterQueryToken.Identifier)
        from op in CollectionOp
        from item in CollectionItemOrValue
        select (QueryNode)new CollectionExprNode(field.Span.ToStringValue(), op, item)
        {
            Position = field.Span.Position.Absolute,
            Length = item.Position + item.Length - field.Span.Position.Absolute,
        };

    private static TokenListParser<FilterQueryToken, QueryNode> FieldExpr { get; } =
        CollectionExpr.Try().Or(ScalarExpr);

    private static TokenListParser<FilterQueryToken, QueryNode> PrimaryExpr { get; } = (
        from open in Token.EqualTo(FilterQueryToken.LeftParen)
        from inner in Parse.Ref(() => OrExpr!)
        from close in Token.EqualTo(FilterQueryToken.RightParen)
        select (QueryNode)new GroupNode(inner)
        {
            Position = open.Span.Position.Absolute,
            Length = close.Span.Position.Absolute + close.Span.Length - open.Span.Position.Absolute,
        }).Or(FieldExpr);

    private static TokenListParser<FilterQueryToken, QueryNode> UnaryExpr { get; } = (
        from notToken in Token.EqualTo(FilterQueryToken.Not)
        from operand in PrimaryExpr
        select (QueryNode)new NotNode(operand)
        {
            Position = notToken.Span.Position.Absolute,
            Length = operand.Position + operand.Length - notToken.Span.Position.Absolute,
        }).Or(PrimaryExpr);

    private static TokenListParser<FilterQueryToken, QueryNode> AndExpr { get; } =
        Parse.Chain(
            Token.EqualTo(FilterQueryToken.And),
            UnaryExpr,
            (op, left, right) =>
            {
                var position = left.Position;
                var length = right.Position + right.Length - position;
                return new BinaryNode(BinaryOperator.And, left, right)
                {
                    Position = position,
                    Length = length,
                };
            });

    private static TokenListParser<FilterQueryToken, QueryNode> OrExpr { get; } =
        Parse.Chain(
            Token.EqualTo(FilterQueryToken.Or),
            AndExpr,
            (op, left, right) =>
            {
                var position = left.Position;
                var length = right.Position + right.Length - position;
                return new BinaryNode(BinaryOperator.Or, left, right)
                {
                    Position = position,
                    Length = length,
                };
            });

    private static TokenListParser<FilterQueryToken, QueryNode> Query { get; } = OrExpr.AtEnd();

    public static TokenListParserResult<FilterQueryToken, QueryNode> ParseQuery(
        this TokenList<FilterQueryToken> tokens)
    {
        return Query(tokens);
    }

    private static TokenListParser<FilterQueryToken, (ScalarOperator Op, int End)> BuildScalarOpParser(
        IReadOnlyList<(FilterQueryToken Token, ScalarOperator Op)> ops)
    {
        var parser = OpWithPosition(ops[0]);
        for (var i = 1; i < ops.Count; i++)
        {
            parser = parser.Or(OpWithPosition(ops[i]));
        }

        return parser;
    }

    private static TokenListParser<FilterQueryToken, (ScalarOperator Op, int End)> OpWithPosition(
        (FilterQueryToken Token, ScalarOperator Op) op)
    {
        return Token.EqualTo(op.Token).Select(t => (op.Op, t.Span.Position.Absolute + t.Span.Length));
    }

    private static string UnescapeString(string span)
    {
        if (span.Length < 2)
        {
            return string.Empty;
        }

        var inner = span.Substring(1, span.Length - 2);

        if (!inner.Contains('\\'))
        {
            return inner;
        }

        var sb = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length)
            {
                i++;
                sb.Append(inner[i] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '"' => '"',
                    '\\' => '\\',
                    _ => inner[i],
                });
            }
            else
            {
                sb.Append(inner[i]);
            }
        }

        return sb.ToString();
    }
}
