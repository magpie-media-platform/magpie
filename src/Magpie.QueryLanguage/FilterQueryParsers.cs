using System.Linq;
using System.Text;

using Superpower;
using Superpower.Parsers;

namespace Magpie.QueryLanguage;

internal static class FilterQueryParsers
{
    public static TokenListParser<FilterQueryToken, ValueNode> StringValue { get; } =
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

    public static TokenListParser<FilterQueryToken, ValueNode> NumberValue { get; } =
        Token.EqualTo(FilterQueryToken.Number)
            .Select(token => (ValueNode)new NumberValueNode(token.Span.ToStringValue())
            {
                Position = token.Span.Position.Absolute,
                Length = token.Span.Length,
            });

    public static TokenListParser<FilterQueryToken, ValueNode> IdentifierValue { get; } =
        Token.EqualTo(FilterQueryToken.Identifier)
            .Select(token => (ValueNode)new IdentifierValueNode(token.Span.ToStringValue())
            {
                Position = token.Span.Position.Absolute,
                Length = token.Span.Length,
            });

    public static TokenListParser<FilterQueryToken, ValueNode> Value { get; } =
        StringValue
            .Or(NumberValue)
            .Or(IdentifierValue);

    public static TokenListParser<FilterQueryToken, ValueNode> ValueList { get; } =
        from open in Token.EqualTo(FilterQueryToken.LeftParen)
        from first in Value
        from rest in Token.EqualTo(FilterQueryToken.Comma).IgnoreThen(Value).Many()
        from close in Token.EqualTo(FilterQueryToken.RightParen)
        select (ValueNode)new ListValueNode([first, .. rest])
        {
            Position = open.Span.Position.Absolute,
            Length = close.Span.Position.Absolute + close.Span.Length - open.Span.Position.Absolute,
        };

    public static TokenListParser<FilterQueryToken, CollectionOperator> CollectionOp { get; } =
        Token.EqualTo(FilterQueryToken.Has).Value(CollectionOperator.Has)
            .Or(Token.EqualTo(FilterQueryToken.NotHas).Value(CollectionOperator.NotHas));

    private static TokenListParser<FilterQueryToken, (ScalarOperator Op, int End)> ScalarOpWithPosition { get; } =
        Token
            .EqualTo(FilterQueryToken.Equals)
                .Select(t => (ScalarOperator.Equals, t.Span.Position.Absolute + t.Span.Length))
            .Or(Token.EqualTo(FilterQueryToken.NotEquals)
                .Select(t => (ScalarOperator.NotEquals, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.GreaterThanOrEqual)
                .Select(t => (ScalarOperator.GreaterThanOrEqual, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.GreaterThan)
                .Select(t => (ScalarOperator.GreaterThan, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.LessThanOrEqual)
                .Select(t => (ScalarOperator.LessThanOrEqual, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.LessThan)
                .Select(t => (ScalarOperator.LessThan, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.Contains)
                .Select(t => (ScalarOperator.Contains, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.NotContains)
                .Select(t => (ScalarOperator.NotContains, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.StartsWith)
                .Select(t => (ScalarOperator.StartsWith, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.NotStartsWith)
                .Select(t => (ScalarOperator.NotStartsWith, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.EndsWith)
                .Select(t => (ScalarOperator.EndsWith, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.NotEndsWith)
                .Select(t => (ScalarOperator.NotEndsWith, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.In)
                .Select(t => (ScalarOperator.In, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.NotIn)
                .Select(t => (ScalarOperator.NotIn, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.Null)
                .Select(t => (ScalarOperator.IsNull, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.NotNull)
                .Select(t => (ScalarOperator.IsNotNull, t.Span.Position.Absolute + t.Span.Length)));

    public static TokenListParser<FilterQueryToken, QueryNode> ScalarExpr { get; } =
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

    public static TokenListParser<FilterQueryToken, ItemExprNode> ItemValue { get; } =
        Value.Select(v => (ItemExprNode)new ItemValueNode(v)
        {
            Position = v.Position,
            Length = v.Length,
        });

    private static TokenListParser<FilterQueryToken, (ScalarOperator Op, int End)> ItemScalarOpWithPosition { get; } =
        Token
            .EqualTo(FilterQueryToken.Equals)
                .Select(t => (ScalarOperator.Equals, t.Span.Position.Absolute + t.Span.Length))
            .Or(Token.EqualTo(FilterQueryToken.NotEquals)
                .Select(t => (ScalarOperator.NotEquals, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.GreaterThanOrEqual)
                .Select(t => (ScalarOperator.GreaterThanOrEqual, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.GreaterThan)
                .Select(t => (ScalarOperator.GreaterThan, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.LessThanOrEqual)
                .Select(t => (ScalarOperator.LessThanOrEqual, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.LessThan)
                .Select(t => (ScalarOperator.LessThan, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.Contains)
                .Select(t => (ScalarOperator.Contains, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.NotContains)
                .Select(t => (ScalarOperator.NotContains, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.StartsWith)
                .Select(t => (ScalarOperator.StartsWith, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.NotStartsWith)
                .Select(t => (ScalarOperator.NotStartsWith, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.EndsWith)
                .Select(t => (ScalarOperator.EndsWith, t.Span.Position.Absolute + t.Span.Length)))
            .Or(Token.EqualTo(FilterQueryToken.NotEndsWith)
                .Select(t => (ScalarOperator.NotEndsWith, t.Span.Position.Absolute + t.Span.Length)));

    public static TokenListParser<FilterQueryToken, ItemExprNode> ItemField { get; } =
        from field in Token.EqualTo(FilterQueryToken.Identifier)
        from opInfo in ItemScalarOpWithPosition
        from value in Value
        select (ItemExprNode)new ItemFieldNode(field.Span.ToStringValue(), opInfo.Op, value)
        {
            Position = field.Span.Position.Absolute,
            Length = value.Position + value.Length - field.Span.Position.Absolute,
        };

    public static TokenListParser<FilterQueryToken, ItemExprNode> ItemGroup { get; } =
        from open in Token.EqualTo(FilterQueryToken.LeftParen)
        from inner in Parse.Ref(() => ItemExpr!)
        from close in Token.EqualTo(FilterQueryToken.RightParen)
        select (ItemExprNode)new ItemGroupNode(inner)
        {
            Position = open.Span.Position.Absolute,
            Length = close.Span.Position.Absolute + close.Span.Length - open.Span.Position.Absolute,
        };

    public static TokenListParser<FilterQueryToken, ItemExprNode> ItemTerm { get; } =
        ItemField.Try()
            .Or(ItemGroup)
            .Or(ItemValue);

    public static TokenListParser<FilterQueryToken, ItemExprNode> ItemAndExpr { get; } =
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

    public static TokenListParser<FilterQueryToken, ItemExprNode> ItemOrExpr { get; } =
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

    public static TokenListParser<FilterQueryToken, ItemExprNode> ItemExpr { get; } = ItemOrExpr;

    private static TokenListParser<FilterQueryToken, ItemExprNode> CollectionItemOrValue { get; } =
        (from open in Token.EqualTo(FilterQueryToken.LeftParen)
         from expr in ItemExpr
         from close in Token.EqualTo(FilterQueryToken.RightParen)
         select expr).Try()
            .Or(ItemValue);

    public static TokenListParser<FilterQueryToken, QueryNode> CollectionExpr { get; } =
        from field in Token.EqualTo(FilterQueryToken.Identifier)
        from op in CollectionOp
        from item in CollectionItemOrValue
        select (QueryNode)new CollectionExprNode(field.Span.ToStringValue(), op, item)
        {
            Position = field.Span.Position.Absolute,
            Length = item.Position + item.Length - field.Span.Position.Absolute,
        };

    public static TokenListParser<FilterQueryToken, QueryNode> FieldExpr { get; } =
        CollectionExpr.Try().Or(ScalarExpr);

    public static TokenListParser<FilterQueryToken, QueryNode> PrimaryExpr { get; } =
        (from open in Token.EqualTo(FilterQueryToken.LeftParen)
         from inner in Parse.Ref(() => OrExpr!)
         from close in Token.EqualTo(FilterQueryToken.RightParen)
         select (QueryNode)new GroupNode(inner)
         {
             Position = open.Span.Position.Absolute,
             Length = close.Span.Position.Absolute + close.Span.Length - open.Span.Position.Absolute,
         }).Or(FieldExpr);

    public static TokenListParser<FilterQueryToken, QueryNode> UnaryExpr { get; } =
        (from notToken in Token.EqualTo(FilterQueryToken.Not)
         from operand in PrimaryExpr
         select (QueryNode)new NotNode(operand)
         {
             Position = notToken.Span.Position.Absolute,
             Length = operand.Position + operand.Length - notToken.Span.Position.Absolute,
         }).Or(PrimaryExpr);

    public static TokenListParser<FilterQueryToken, QueryNode> AndExpr { get; } =
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

    public static TokenListParser<FilterQueryToken, QueryNode> OrExpr { get; } =
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

    public static TokenListParser<FilterQueryToken, QueryNode> Query { get; } = OrExpr.AtEnd();

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
