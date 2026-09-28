using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.QueryLanguage;

internal sealed class ItemFieldInfo(
    string propertyName,
    Type valueType,
    ISet<CriteriaValueOperator> supportedOperators,
    bool requireAbsoluteUri = false)
{
    public string PropertyName { get; } = propertyName;
    public Type ValueType { get; } = valueType;

    public ISet<CriteriaValueOperator> SupportedOperators { get; } = supportedOperators;

    public bool RequireAbsoluteUri { get; } = requireAbsoluteUri;
}
