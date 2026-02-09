using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Magpie.Models.Data;
using Magpie.Models.Enums;

namespace Magpie.QueryLanguage;

internal static class FilterQueryTransform
{
    public static ParseResult<ContentFilterCriteria> ToContentFilterCriteria(QueryNode node)
    {
        var context = new ContentTransformContext();
        var error = TransformNode(node, context);
        if (error != null)
        {
            return ParseResult<ContentFilterCriteria>.Failure(error.Value);
        }

        return ParseResult<ContentFilterCriteria>.Success(context.Build());
    }

    public static ParseResult<SubjectFilterCriteria> ToSubjectFilterCriteria(QueryNode node)
    {
        var context = new SubjectTransformContext();
        var error = TransformSubjectNode(node, context);
        if (error != null)
        {
            return ParseResult<SubjectFilterCriteria>.Failure(error.Value);
        }

        return ParseResult<SubjectFilterCriteria>.Success(context.Build());
    }

    private static ParseError? TransformNode(QueryNode node, ContentTransformContext context)
    {
        return node switch
        {
            BinaryNode binary => TransformBinary(binary, context),
            NotNode not => TransformNot(not, context),
            GroupNode group => TransformNode(group.Inner, context),
            ScalarExprNode scalar => TransformScalar(scalar, context),
            CollectionExprNode collection => TransformCollection(collection, context),
            _ => new ParseError(
                $"Unsupported node type: {node.GetType().Name}",
                node.Position,
                [])
        };
    }

    private static ParseError? TransformBinary(BinaryNode node, ContentTransformContext context)
    {
        if (node.Operator == BinaryOperator.Or)
        {
            return TransformOrBinary(node, context);
        }

        var leftError = TransformNode(node.Left, context);
        if (leftError != null)
        {
            return leftError;
        }

        return TransformNode(node.Right, context);
    }

    private static ParseError? TransformOrBinary(BinaryNode node, ContentTransformContext context)
    {
        var collectionNodes = FlattenOrChain(node);

        if (collectionNodes.Count > 0 && collectionNodes.All(n => n is CollectionExprNode))
        {
            var firstField = ((CollectionExprNode)collectionNodes[0]).Field.ToLowerInvariant();
            var sameField = collectionNodes.All(n =>
                n is CollectionExprNode c && c.Field.ToLowerInvariant() == firstField);

            if (sameField)
            {
                var collections = collectionNodes.Cast<CollectionExprNode>().ToList();
                return TransformCollectionOr(collections, context);
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
        FlattenOrChainRecursive(node, nodes);
        return nodes;
    }

    private static void FlattenOrChainRecursive(QueryNode node, List<QueryNode> nodes)
    {
        if (node is BinaryNode binary && binary.Operator == BinaryOperator.Or)
        {
            FlattenOrChainRecursive(binary.Left, nodes);
            FlattenOrChainRecursive(binary.Right, nodes);
        }
        else if (node is GroupNode group)
        {
            FlattenOrChainRecursive(group.Inner, nodes);
        }
        else
        {
            nodes.Add(node);
        }
    }

    private static ParseError? TransformNot(NotNode node, ContentTransformContext context)
    {
        return new ParseError(
            "NOT expressions are not supported; use !has or != operators instead",
            node.Position,
            []);
    }

    private static ParseError? TransformScalar(ScalarExprNode node, ContentTransformContext context)
    {
        var fieldLower = node.Field.ToLowerInvariant();
        var field = FieldRegistry.GetContentField(fieldLower);
        if (field == null)
        {
            return new ParseError(
                $"Unknown field: '{node.Field}'",
                node.Position,
                []);
        }

        var operatorResult = MapScalarOperator(node.Operator);
        if (!operatorResult.HasValue)
        {
            return new ParseError(
                $"Unsupported operator: {node.Operator}",
                node.Position,
                []);
        }

        var op = operatorResult.Value;

        if (op == CriteriaValueOperator.In || op == CriteriaValueOperator.NotIn)
        {
            if (node.Value is not ListValueNode listValue)
            {
                return new ParseError(
                    "Expected value list for in/!in operator",
                    node.Position,
                    []);
            }

            var values = new List<object>();
            foreach (var item in listValue.Values)
            {
                var parsed = ParseValue(item, field.ValueType);
                if (parsed == null && !IsNullIdentifier(item))
                {
                    return new ParseError(
                        $"Cannot parse value for field '{node.Field}'",
                        item.Position,
                        []);
                }

                values.Add(parsed!);
            }

            field.AddCriterion(context, op, null, values);
        }
        else if (op == CriteriaValueOperator.IsNull || op == CriteriaValueOperator.IsNotNull)
        {
            field.AddCriterion(context, op, null, null);
        }
        else
        {
            if (node.Value == null)
            {
                return new ParseError(
                    "Expected value",
                    node.Position,
                    []);
            }

            var parsedValue = ParseValue(node.Value, field.ValueType);
            if (parsedValue == null && !IsNullIdentifier(node.Value))
            {
                return new ParseError(
                    $"Cannot parse value for field '{node.Field}'",
                    node.Value.Position,
                    []);
            }

            field.AddCriterion(context, op, parsedValue, null);
        }

        return null;
    }

    private static ParseError? TransformCollection(CollectionExprNode node, ContentTransformContext context)
    {
        return TransformCollectionOr([node], context);
    }

    private static ParseError? TransformCollectionOr(List<CollectionExprNode> nodes, ContentTransformContext context)
    {
        if (nodes.Count == 0)
        {
            return null;
        }

        var fieldLower = nodes[0].Field.ToLowerInvariant();
        var field = FieldRegistry.GetContentCollectionField(fieldLower);
        if (field == null)
        {
            return new ParseError(
                $"Unknown collection field: '{nodes[0].Field}'",
                nodes[0].Position,
                []);
        }

        foreach (var node in nodes)
        {
            var itemCriteriaResult = BuildCollectionItemCriteria(node.Item, field);
            if (!itemCriteriaResult.IsSuccess)
            {
                return itemCriteriaResult.Error!.Value;
            }

            if (node.Operator == CollectionOperator.Has)
            {
                field.AddSome(context, itemCriteriaResult.Value!);
            }
            else
            {
                field.AddNone(context, itemCriteriaResult.Value!);
            }
        }

        return null;
    }

    private static ParseResult<object> BuildCollectionItemCriteria(ItemExprNode item, CollectionFieldInfo field)
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

    private static ParseResult<object> BuildSimpleItemCriteria(ItemValueNode node, CollectionFieldInfo field)
    {
        var value = ParseValue(node.Value, field.PrimaryFieldType);
        if (value == null && !IsNullIdentifier(node.Value))
        {
            return ParseResult<object>.Failure(new ParseError(
                $"Cannot parse value for collection '{field.QueryName}'",
                node.Value.Position,
                []));
        }

        return ParseResult<object>.Success(field.CreateSimpleCriteria(value));
    }

    private static ParseResult<object> BuildFieldItemCriteria(ItemFieldNode node, CollectionFieldInfo field)
    {
        var itemField = field.GetItemField(node.Field.ToLowerInvariant());
        if (itemField == null)
        {
            return ParseResult<object>.Failure(new ParseError(
                $"Unknown field '{node.Field}' in collection '{field.QueryName}'",
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

        var value = ParseValue(node.Value, itemField.ValueType);
        if (value == null && !IsNullIdentifier(node.Value))
        {
            return ParseResult<object>.Failure(new ParseError(
                $"Cannot parse value for field '{node.Field}'",
                node.Value.Position,
                []));
        }

        return ParseResult<object>.Success(field.CreateFieldCriteria(itemField.PropertyName, op.Value, value));
    }

    private static ParseResult<object> BuildBinaryItemCriteria(ItemBinaryNode node, CollectionFieldInfo field)
    {
        if (node.Operator == BinaryOperator.Or)
        {
            return ParseResult<object>.Failure(new ParseError(
                "OR within a single collection item is not supported; use separate has clauses",
                node.Position,
                []));
        }

        var leftResult = BuildCollectionItemCriteria(node.Left, field);
        if (!leftResult.IsSuccess)
        {
            return leftResult;
        }

        var rightResult = BuildCollectionItemCriteria(node.Right, field);
        if (!rightResult.IsSuccess)
        {
            return rightResult;
        }

        return ParseResult<object>.Success(field.MergeCriteria(leftResult.Value!, rightResult.Value!));
    }

    private static ParseError? TransformSubjectNode(QueryNode node, SubjectTransformContext context)
    {
        return node switch
        {
            BinaryNode binary => TransformSubjectBinary(binary, context),
            NotNode not => TransformSubjectNot(not, context),
            GroupNode group => TransformSubjectNode(group.Inner, context),
            ScalarExprNode scalar => TransformSubjectScalar(scalar, context),
            CollectionExprNode collection => TransformSubjectCollection(collection, context),
            _ => new ParseError(
                $"Unsupported node type: {node.GetType().Name}",
                node.Position,
                [])
        };
    }

    private static ParseError? TransformSubjectBinary(BinaryNode node, SubjectTransformContext context)
    {
        if (node.Operator == BinaryOperator.Or)
        {
            return TransformSubjectOrBinary(node, context);
        }

        var leftError = TransformSubjectNode(node.Left, context);
        if (leftError != null)
        {
            return leftError;
        }

        return TransformSubjectNode(node.Right, context);
    }

    private static ParseError? TransformSubjectOrBinary(BinaryNode node, SubjectTransformContext context)
    {
        var collectionNodes = FlattenOrChain(node);

        if (collectionNodes.Count > 0 && collectionNodes.All(n => n is CollectionExprNode))
        {
            var firstField = ((CollectionExprNode)collectionNodes[0]).Field.ToLowerInvariant();
            var sameField = collectionNodes.All(n =>
                n is CollectionExprNode c && c.Field.ToLowerInvariant() == firstField);

            if (sameField)
            {
                var collections = collectionNodes.Cast<CollectionExprNode>().ToList();
                return TransformSubjectCollectionOr(collections, context);
            }
        }

        return new ParseError(
            "OR expressions are only supported for collection fields on the same collection",
            node.Position,
            []);
    }

    private static ParseError? TransformSubjectNot(NotNode node, SubjectTransformContext context)
    {
        return new ParseError(
            "NOT expressions are not supported; use !has or != operators instead",
            node.Position,
            []);
    }

    private static ParseError? TransformSubjectScalar(ScalarExprNode node, SubjectTransformContext context)
    {
        var fieldLower = node.Field.ToLowerInvariant();
        var field = FieldRegistry.GetSubjectField(fieldLower);
        if (field == null)
        {
            return new ParseError(
                $"Unknown field: '{node.Field}'",
                node.Position,
                []);
        }

        var operatorResult = MapScalarOperator(node.Operator);
        if (!operatorResult.HasValue)
        {
            return new ParseError(
                $"Unsupported operator: {node.Operator}",
                node.Position,
                []);
        }

        var op = operatorResult.Value;

        if (op == CriteriaValueOperator.In || op == CriteriaValueOperator.NotIn)
        {
            if (node.Value is not ListValueNode listValue)
            {
                return new ParseError(
                    "Expected value list for in/!in operator",
                    node.Position,
                    []);
            }

            var values = new List<object>();
            foreach (var item in listValue.Values)
            {
                var parsed = ParseValue(item, field.ValueType);
                if (parsed == null && !IsNullIdentifier(item))
                {
                    return new ParseError(
                        $"Cannot parse value for field '{node.Field}'",
                        item.Position,
                        []);
                }

                values.Add(parsed!);
            }

            field.AddCriterion(context, op, null, values);
        }
        else if (op == CriteriaValueOperator.IsNull || op == CriteriaValueOperator.IsNotNull)
        {
            field.AddCriterion(context, op, null, null);
        }
        else
        {
            if (node.Value == null)
            {
                return new ParseError(
                    "Expected value",
                    node.Position,
                    []);
            }

            var parsedValue = ParseValue(node.Value, field.ValueType);
            if (parsedValue == null && !IsNullIdentifier(node.Value))
            {
                return new ParseError(
                    $"Cannot parse value for field '{node.Field}'",
                    node.Value.Position,
                    []);
            }

            field.AddCriterion(context, op, parsedValue, null);
        }

        return null;
    }

    private static ParseError? TransformSubjectCollection(CollectionExprNode node, SubjectTransformContext context)
    {
        return TransformSubjectCollectionOr([node], context);
    }

    private static ParseError? TransformSubjectCollectionOr(
        List<CollectionExprNode> nodes,
        SubjectTransformContext context)
    {
        if (nodes.Count == 0)
        {
            return null;
        }

        var fieldLower = nodes[0].Field.ToLowerInvariant();
        var field = FieldRegistry.GetSubjectCollectionField(fieldLower);
        if (field == null)
        {
            return new ParseError(
                $"Unknown collection field: '{nodes[0].Field}'",
                nodes[0].Position,
                []);
        }

        foreach (var node in nodes)
        {
            var itemCriteriaResult = BuildSubjectCollectionItemCriteria(node.Item, field);
            if (!itemCriteriaResult.IsSuccess)
            {
                return itemCriteriaResult.Error!.Value;
            }

            if (node.Operator == CollectionOperator.Has)
            {
                field.AddSome(context, itemCriteriaResult.Value!);
            }
            else
            {
                field.AddNone(context, itemCriteriaResult.Value!);
            }
        }

        return null;
    }

    private static ParseResult<object> BuildSubjectCollectionItemCriteria(
        ItemExprNode item,
        SubjectCollectionFieldInfo field)
    {
        return item switch
        {
            ItemValueNode valueNode => BuildSubjectSimpleItemCriteria(valueNode, field),
            ItemFieldNode fieldNode => BuildSubjectFieldItemCriteria(fieldNode, field),
            ItemBinaryNode binaryNode => BuildSubjectBinaryItemCriteria(binaryNode, field),
            ItemGroupNode groupNode => BuildSubjectCollectionItemCriteria(groupNode.Inner, field),
            _ => ParseResult<object>.Failure(new ParseError(
                $"Unsupported item expression type: {item.GetType().Name}",
                item.Position,
                []))
        };
    }

    private static ParseResult<object> BuildSubjectSimpleItemCriteria(
        ItemValueNode node,
        SubjectCollectionFieldInfo field)
    {
        var value = ParseValue(node.Value, field.PrimaryFieldType);
        if (value == null && !IsNullIdentifier(node.Value))
        {
            return ParseResult<object>.Failure(new ParseError(
                $"Cannot parse value for collection '{field.QueryName}'",
                node.Value.Position,
                []));
        }

        return ParseResult<object>.Success(field.CreateSimpleCriteria(value));
    }

    private static ParseResult<object> BuildSubjectFieldItemCriteria(
        ItemFieldNode node,
        SubjectCollectionFieldInfo field)
    {
        var itemField = field.GetItemField(node.Field.ToLowerInvariant());
        if (itemField == null)
        {
            return ParseResult<object>.Failure(new ParseError(
                $"Unknown field '{node.Field}' in collection '{field.QueryName}'",
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

        var value = ParseValue(node.Value, itemField.ValueType);
        if (value == null && !IsNullIdentifier(node.Value))
        {
            return ParseResult<object>.Failure(new ParseError(
                $"Cannot parse value for field '{node.Field}'",
                node.Value.Position,
                []));
        }

        return ParseResult<object>.Success(field.CreateFieldCriteria(itemField.PropertyName, op.Value, value));
    }

    private static ParseResult<object> BuildSubjectBinaryItemCriteria(
        ItemBinaryNode node,
        SubjectCollectionFieldInfo field)
    {
        if (node.Operator == BinaryOperator.Or)
        {
            return ParseResult<object>.Failure(new ParseError(
                "OR within a single collection item is not supported; use separate has clauses",
                node.Position,
                []));
        }

        var leftResult = BuildSubjectCollectionItemCriteria(node.Left, field);
        if (!leftResult.IsSuccess)
        {
            return leftResult;
        }

        var rightResult = BuildSubjectCollectionItemCriteria(node.Right, field);
        if (!rightResult.IsSuccess)
        {
            return rightResult;
        }

        return ParseResult<object>.Success(field.MergeCriteria(leftResult.Value!, rightResult.Value!));
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
        return node is IdentifierValueNode { Value: var v }
            && v.Equals("null", StringComparison.OrdinalIgnoreCase);
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
        if (targetType == typeof(string))
        {
            return value;
        }

        if (targetType == typeof(DateTime))
        {
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return dt;
            }

            return null;
        }

        if (targetType == typeof(TimeSpan))
        {
            if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var ts))
            {
                return ts;
            }

            return ParseShortTimeSpan(value);
        }

        if (targetType == typeof(Guid))
        {
            if (Guid.TryParse(value, out var guid))
            {
                return guid;
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// Parses short-form time spans like "1h30m", "90m", or "2h". Seconds are intentionally
    /// ignored (minute granularity). Trailing bare numbers are treated as minutes.
    /// </summary>
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
        if (targetType == typeof(int))
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            {
                return i;
            }

            return null;
        }

        if (targetType == typeof(long))
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            {
                return l;
            }

            return null;
        }

        if (targetType == typeof(double))
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                return d;
            }

            return null;
        }

        if (targetType == typeof(string))
        {
            return value;
        }

        return null;
    }

    private static object? ParseIdentifierValue(string value, Type targetType)
    {
        if (value.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            if (targetType == typeof(bool))
            {
                return true;
            }

            return null;
        }

        if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            if (targetType == typeof(bool))
            {
                return false;
            }

            return null;
        }

        if (targetType.IsEnum)
        {
            try
            {
                return Enum.Parse(targetType, value, ignoreCase: true);
            }
            catch
            {
                return null;
            }
        }

        if (targetType == typeof(string))
        {
            return value;
        }

        return null;
    }
}
