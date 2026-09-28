using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.QueryLanguage;

internal sealed class CollectionFieldDefinition(
    string queryName,
    Type primaryFieldType,
    Dictionary<string, ItemFieldInfo> itemFields,
    Func<object?, object> createSimpleCriteria,
    Func<string, CriteriaValueOperator, object?, object> createFieldCriteria,
    Func<object, object, object> mergeCriteria)
{
    public string QueryName { get; } = queryName;
    public Type PrimaryFieldType { get; } = primaryFieldType;
    public IReadOnlyDictionary<string, ItemFieldInfo> ItemFields { get; } = itemFields;

    public ItemFieldInfo? GetItemField(string name)
    {
        return itemFields.GetValueOrDefault(name);
    }

    public object CreateSimpleCriteria(object? value)
    {
        return createSimpleCriteria(value);
    }

    public object CreateFieldCriteria(string propertyName, CriteriaValueOperator op, object? value)
    {
        return createFieldCriteria(propertyName, op, value);
    }

    public object MergeCriteria(object a, object b)
    {
        return mergeCriteria(a, b);
    }
}
