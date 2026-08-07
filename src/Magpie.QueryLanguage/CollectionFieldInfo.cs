using System;
using System.Collections.Generic;

namespace Magpie.QueryLanguage;

internal sealed class CollectionFieldInfo<TContext>(
    CollectionFieldDefinition definition,
    Action<TContext, IReadOnlyList<object>, bool> addToContext)
{
    public CollectionFieldDefinition Definition { get; } = definition;

    public void AddSomeGroup(TContext context, IReadOnlyList<object> group)
    {
        addToContext(context, group, true);
    }

    public void AddNone(TContext context, object criteria)
    {
        addToContext(context, [criteria], false);
    }
}
