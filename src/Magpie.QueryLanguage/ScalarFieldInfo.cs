using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.QueryLanguage;

internal abstract class ScalarFieldInfo<TContext>(Type valueType)
{
    public Type ValueType { get; } = valueType;

    public abstract void AddCriterion(
        TContext context,
        CriteriaValueOperator op,
        object? value,
        IEnumerable<object>? values);
}
