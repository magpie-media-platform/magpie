using System;

namespace Magpie.QueryLanguage;

internal sealed class ItemFieldInfo(string propertyName, Type valueType)
{
    public string PropertyName { get; } = propertyName;
    public Type ValueType { get; } = valueType;
}
