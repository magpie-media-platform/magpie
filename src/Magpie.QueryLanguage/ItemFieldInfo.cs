using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.QueryLanguage;

internal sealed class ItemFieldInfo(
    string propertyName,
    Type valueType,
    ISet<CriteriaValueOperator>? unsupportedOperators = null,
    bool requireAbsoluteUri = false)
{
    public string PropertyName { get; } = propertyName;
    public Type ValueType { get; } = valueType;

    public ISet<CriteriaValueOperator> UnsupportedOperators { get; } =
        unsupportedOperators ?? new HashSet<CriteriaValueOperator>();

    public bool RequireAbsoluteUri { get; } = requireAbsoluteUri;
}
