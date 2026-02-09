using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.QueryLanguage;

internal abstract class SubjectScalarFieldInfo(string queryName, Type valueType)
{
    public string QueryName { get; } = queryName;
    public Type ValueType { get; } = valueType;

    public abstract void AddCriterion(
        SubjectTransformContext context,
        CriteriaValueOperator op,
        object? value,
        IEnumerable<object>? values);
}
