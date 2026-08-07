using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.QueryLanguage;

internal static class QueryNodeTransformExtensions
{
    private static readonly HashSet<string> MediaFileFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(MediaFileCriteria.FileKind),
        nameof(MediaFileCriteria.MimeType),
        nameof(MediaFileCriteria.FilePath),
        nameof(MediaFileCriteria.FileSize),
    };

    public static ParseResult<ContentFilterCriteria> ToContentFilterCriteria(this QueryNode node)
    {
        var context = new ContentTransformContext();
        var error = TransformNode(
            node,
            context,
            name => name.GetContentField(),
            name => name.GetContentCollectionField());
        if (error != null)
        {
            return ParseResult<ContentFilterCriteria>.Failure(error.Value);
        }

        return ParseResult<ContentFilterCriteria>.Success(context.Build());
    }

    public static ParseResult<SubjectFilterCriteria> ToSubjectFilterCriteria(this QueryNode node)
    {
        var context = new SubjectTransformContext();
        var error = TransformNode(
            node,
            context,
            name => name.GetSubjectField(),
            name => name.GetSubjectCollectionField());
        if (error != null)
        {
            return ParseResult<SubjectFilterCriteria>.Failure(error.Value);
        }

        return ParseResult<SubjectFilterCriteria>.Success(context.Build());
    }

    private static ParseError? TransformNode<TContext>(
        QueryNode node,
        TContext context,
        Func<string, ScalarFieldInfo<TContext>?> getScalarField,
        Func<string, CollectionFieldInfo<TContext>?> getCollectionField)
    {
        var pending = new Stack<QueryNode>();
        pending.Push(node);

        while (pending.Count > 0)
        {
            ParseError? error;
            switch (pending.Pop())
            {
                case BinaryNode { Operator: BinaryOperator.And } binary:
                    pending.Push(binary.Right);
                    pending.Push(binary.Left);
                    continue;
                case GroupNode group:
                    pending.Push(group.Inner);
                    continue;
                case BinaryNode binary:
                    error = TransformOrBinary(binary, context, getCollectionField);
                    break;
                case NotNode not:
                    error = new ParseError(
                        "The query does not support NOT expressions. Use !has or != instead.",
                        not.Position,
                        []);
                    break;
                case ScalarExprNode scalar:
                    error = TransformScalar(scalar, context, getScalarField);
                    break;
                case CollectionExprNode collection:
                    error = TransformCollectionOr([collection], context, getCollectionField);
                    break;
                case var other:
                    error = new ParseError($"Unsupported node type: {other.GetType().Name}", other.Position, []);
                    break;
            }

            if (error != null)
            {
                return error;
            }
        }

        return null;
    }

    private static ParseError? TransformOrBinary<TContext>(
        BinaryNode node,
        TContext context,
        Func<string, CollectionFieldInfo<TContext>?> getCollectionField)
    {
        var flattened = FlattenOrChain(node);
        var collections = flattened.OfType<CollectionExprNode>().ToList();

        if (collections.Count > 0 && collections.Count == flattened.Count)
        {
            var firstField = collections[0].Field.ToLowerInvariant();
            if (collections.All(c => c.Field.ToLowerInvariant() == firstField))
            {
                return TransformCollectionOr(collections, context, getCollectionField);
            }
        }

        return new ParseError(
            "OR expressions are only supported for collection fields on the same collection",
            node.Position,
            []);
    }

    private static List<QueryNode> FlattenOrChain(BinaryNode node)
    {
        var nodes = new List<QueryNode>();
        var pending = new Stack<QueryNode>();
        pending.Push(node);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current is BinaryNode { Operator: BinaryOperator.Or } binary)
            {
                pending.Push(binary.Right);
                pending.Push(binary.Left);
            }
            else if (current is GroupNode group)
            {
                pending.Push(group.Inner);
            }
            else
            {
                nodes.Add(current);
            }
        }

        return nodes;
    }

    private static ParseError? TransformScalar<TContext>(
        ScalarExprNode node,
        TContext context,
        Func<string, ScalarFieldInfo<TContext>?> getScalarField)
    {
        var field = getScalarField(node.Field);
        if (field == null)
        {
            if (MediaFileFieldNames.Contains(node.Field))
            {
                return new ParseError($"Media file field {node.Field} is not supported yet", node.Position, []);
            }

            return new ParseError($"Unknown field: {node.Field}", node.Position, []);
        }

        var operatorResult = MapScalarOperator(node.Operator);
        if (!operatorResult.HasValue)
        {
            return new ParseError($"Unsupported operator: {node.Operator}", node.Position, []);
        }

        var op = operatorResult.Value;

        if (op is CriteriaValueOperator.In or CriteriaValueOperator.NotIn)
        {
            if (node.Value is not ListValueNode listValue)
            {
                return new ParseError("Expected value list for in/!in operator", node.Position, []);
            }

            var values = new List<object>();
            foreach (var item in listValue.Values)
            {
                var parsed = ParseValue(item, field.ValueType);
                if (parsed == null && !IsNullIdentifier(item))
                {
                    return new ParseError($"Cannot parse value for field {node.Field}", item.Position, []);
                }

                values.Add(parsed!);
            }

            field.AddCriterion(context, op, null, values);
        }
        else if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            field.AddCriterion(context, op, null, null);
        }
        else
        {
            if (node.Value == null)
            {
                return new ParseError("Expected value", node.Position, []);
            }

            var parsedValue = ParseValue(node.Value, field.ValueType);
            if (parsedValue == null && !IsNullIdentifier(node.Value))
            {
                return new ParseError($"Cannot parse value for field {node.Field}", node.Value.Position, []);
            }

            field.AddCriterion(context, op, parsedValue, null);
        }

        return null;
    }

    private static ParseError? TransformCollectionOr<TContext>(
        List<CollectionExprNode> nodes,
        TContext context,
        Func<string, CollectionFieldInfo<TContext>?> getCollectionField)
    {
        if (nodes.Count == 0)
        {
            return null;
        }

        if (nodes.Count > 1)
        {
            var notHasNode = nodes.FirstOrDefault(n => n.Operator == CollectionOperator.NotHas);
            if (notHasNode != null)
            {
                return new ParseError(
                    "The query cannot combine '!has' with 'or'. Use separate 'and' clauses.",
                    notHasNode.Position,
                    []);
            }
        }

        var field = getCollectionField(nodes[0].Field);
        if (field == null)
        {
            return new ParseError($"Unknown collection field: {nodes[0].Field}", nodes[0].Position, []);
        }

        var someGroup = new List<object>();
        foreach (var node in nodes)
        {
            var itemCriteriaResult = BuildCollectionItemCriteria(node.Item, field.Definition);
            if (!itemCriteriaResult.IsSuccess)
            {
                return itemCriteriaResult.Error!.Value;
            }

            if (node.Operator == CollectionOperator.Has)
            {
                someGroup.Add(itemCriteriaResult.Value!);
            }
            else
            {
                field.AddNone(context, itemCriteriaResult.Value!);
            }
        }

        if (someGroup.Count > 0)
        {
            field.AddSomeGroup(context, someGroup);
        }

        return null;
    }

    private static ParseResult<object> BuildCollectionItemCriteria(ItemExprNode item, CollectionFieldDefinition field)
    {
        return item switch
        {
            ItemValueNode valueNode => BuildSimpleItemCriteria(valueNode, field),
            ItemFieldNode fieldNode => BuildFieldItemCriteria(fieldNode, field),
            ItemBinaryNode binaryNode => BuildBinaryItemCriteria(binaryNode, field),
            ItemGroupNode groupNode => BuildCollectionItemCriteria(groupNode.Inner, field),
            _ => ParseResult<object>.Failure(new ParseError(
                $"Unsupported item expression type: {item.GetType().Name}",
                item.Position,
                []))
        };
    }

    private static ParseResult<object> BuildSimpleItemCriteria(ItemValueNode node, CollectionFieldDefinition field)
    {
        var value = ParseValue(node.Value, field.PrimaryFieldType);
        if (value == null && !IsNullIdentifier(node.Value))
        {
            return ParseResult<object>.Failure(new ParseError(
                $"Cannot parse value for collection {field.QueryName}",
                node.Value.Position,
                []));
        }

        return ParseResult<object>.Success(field.CreateSimpleCriteria(value));
    }

    private static ParseResult<object> BuildFieldItemCriteria(ItemFieldNode node, CollectionFieldDefinition field)
    {
        var itemField = field.GetItemField(node.Field);
        if (itemField == null)
        {
            return ParseResult<object>.Failure(new ParseError(
                $"Unknown field {node.Field} in collection {field.QueryName}",
                node.Position,
                []));
        }

        var op = MapScalarOperator(node.Operator);
        if (!op.HasValue)
        {
            return ParseResult<object>.Failure(new ParseError(
                $"Unsupported operator: {node.Operator}",
                node.Position,
                []));
        }

        if (itemField.UnsupportedOperators.Contains(op.Value))
        {
            return ParseResult<object>.Failure(new ParseError(
                $"Operator {node.Operator} is not supported for field {node.Field}",
                node.Position,
                []));
        }

        var value = ParseValue(node.Value, itemField.ValueType);
        if (value == null && !IsNullIdentifier(node.Value))
        {
            return ParseResult<object>.Failure(new ParseError(
                $"Cannot parse value for field {node.Field}",
                node.Value.Position,
                []));
        }

        var uriError = ValidateAbsoluteUri(itemField, op.Value, value, node);
        if (uriError != null)
        {
            return ParseResult<object>.Failure(uriError.Value);
        }

        return ParseResult<object>.Success(field.CreateFieldCriteria(itemField.PropertyName, op.Value, value));
    }

    private static ParseResult<object> BuildBinaryItemCriteria(ItemBinaryNode node, CollectionFieldDefinition field)
    {
        var leaves = new List<ItemExprNode>();
        var pending = new Stack<ItemExprNode>();
        pending.Push(node);

        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case ItemBinaryNode { Operator: BinaryOperator.Or } orNode:
                    return ParseResult<object>.Failure(new ParseError(
                        "A collection item does not support OR. Use separate has clauses.",
                        orNode.Position,
                        []));
                case ItemBinaryNode binary:
                    pending.Push(binary.Right);
                    pending.Push(binary.Left);
                    break;
                case ItemGroupNode group:
                    pending.Push(group.Inner);
                    break;
                case var leaf:
                    leaves.Add(leaf);
                    break;
            }
        }

        object? merged = null;
        foreach (var leaf in leaves)
        {
            var leafResult = BuildCollectionItemCriteria(leaf, field);
            if (!leafResult.IsSuccess)
            {
                return leafResult;
            }

            merged = merged == null ? leafResult.Value : field.MergeCriteria(merged, leafResult.Value!);
        }

        return ParseResult<object>.Success(merged!);
    }

    private static CriteriaValueOperator? MapScalarOperator(ScalarOperator op)
    {
        return op switch
        {
            ScalarOperator.Equals => CriteriaValueOperator.Equals,
            ScalarOperator.NotEquals => CriteriaValueOperator.NotEquals,
            ScalarOperator.GreaterThan => CriteriaValueOperator.GreaterThan,
            ScalarOperator.GreaterThanOrEqual => CriteriaValueOperator.GreaterThanOrEqual,
            ScalarOperator.LessThan => CriteriaValueOperator.LessThan,
            ScalarOperator.LessThanOrEqual => CriteriaValueOperator.LessThanOrEqual,
            ScalarOperator.Contains => CriteriaValueOperator.ContainsString,
            ScalarOperator.NotContains => CriteriaValueOperator.NotContainsString,
            ScalarOperator.StartsWith => CriteriaValueOperator.StartsWithString,
            ScalarOperator.NotStartsWith => CriteriaValueOperator.NotStartsWithString,
            ScalarOperator.EndsWith => CriteriaValueOperator.EndsWithString,
            ScalarOperator.NotEndsWith => CriteriaValueOperator.NotEndsWithString,
            ScalarOperator.In => CriteriaValueOperator.In,
            ScalarOperator.NotIn => CriteriaValueOperator.NotIn,
            ScalarOperator.IsNull => CriteriaValueOperator.IsNull,
            ScalarOperator.IsNotNull => CriteriaValueOperator.IsNotNull,
            _ => null
        };
    }

    private static bool IsNullIdentifier(ValueNode node)
    {
        return node is IdentifierValueNode { Value: var v } && v.Equals("null", StringComparison.OrdinalIgnoreCase);
    }

    private static ParseError? ValidateAbsoluteUri(
        ItemFieldInfo itemField,
        CriteriaValueOperator op,
        object? value,
        ItemFieldNode node)
    {
        if (!itemField.RequireAbsoluteUri ||
            op == CriteriaValueOperator.IsNull ||
            op == CriteriaValueOperator.IsNotNull)
        {
            return null;
        }

        if (value is string text && !Uri.TryCreate(text, UriKind.Absolute, out _))
        {
            return new ParseError($"Value for field {node.Field} must be an absolute url", node.Value.Position, []);
        }

        return null;
    }

    private static object? ParseValue(ValueNode node, Type targetType)
    {
        return node switch
        {
            StringValueNode str => ParseStringValue(str.Value, targetType),
            NumberValueNode num => ParseNumberValue(num.Value, targetType),
            IdentifierValueNode id => ParseIdentifierValue(id.Value, targetType),
            _ => null
        };
    }

    private static object? ParseStringValue(string value, Type targetType)
    {
        return targetType switch
        {
            _ when targetType == typeof(string) => value,
            _ when targetType == typeof(DateTime) =>
                DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null,
            _ when targetType == typeof(TemporalValue) =>
                TemporalValue.TryParse(value, out var temporal) ? temporal : null,
            _ when targetType == typeof(TimeSpan) =>
                TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var ts) ? ts : ParseShortTimeSpan(value),
            _ when targetType == typeof(Guid) => Guid.TryParse(value, out var guid) ? guid : null,
            _ => null
        };
    }

    private static TimeSpan? ParseShortTimeSpan(string value)
    {
        var totalMinutes = 0;
        var current = 0;
        var i = 0;

        while (i < value.Length)
        {
            if (char.IsDigit(value[i]))
            {
                current = current * 10 + (value[i] - '0');
                i++;
            }
            else if (value[i] == 'h')
            {
                totalMinutes += current * 60;
                current = 0;
                i++;
            }
            else if (value[i] == 'm')
            {
                totalMinutes += current;
                current = 0;
                i++;
            }
            else if (value[i] == 's')
            {
                current = 0;
                i++;
            }
            else if (char.IsWhiteSpace(value[i]))
            {
                i++;
            }
            else
            {
                return null;
            }
        }

        totalMinutes += current;

        if (totalMinutes == 0)
        {
            return null;
        }

        return TimeSpan.FromMinutes(totalMinutes);
    }

    private static object? ParseNumberValue(string value, Type targetType)
    {
        return targetType switch
        {
            _ when targetType == typeof(int) =>
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null,
            _ when targetType == typeof(long) =>
                long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null,
            _ when targetType == typeof(double) =>
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
            _ when targetType == typeof(string) => value,
            _ => null
        };
    }

    private static object? ParseIdentifierValue(string value, Type targetType)
    {
        if (value.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return targetType == typeof(bool) ? value.Equals("true", StringComparison.OrdinalIgnoreCase) : null;
        }

        if (targetType.IsEnum)
        {
            return Enum.TryParse(targetType, value, ignoreCase: true, out var parsed) ? parsed : null;
        }

        return targetType == typeof(string) ? value : null;
    }
}
