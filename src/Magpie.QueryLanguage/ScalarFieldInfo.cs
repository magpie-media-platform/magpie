using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.QueryLanguage;

internal abstract class ScalarFieldInfo<TContext>(Type valueType)
{
    public Type ValueType { get; } = valueType;

    public virtual bool SupportsOperator(CriteriaValueOperator op)
    {
        return true;
    }

    public virtual bool ConflictsWithExisting(TContext context, CriteriaValueOperator op, object? value)
    {
        return false;
    }

    public abstract void AddCriterion(
        TContext context,
        CriteriaValueOperator op,
        object? value,
        IEnumerable<object>? values);
}
