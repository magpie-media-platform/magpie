using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class SubjectEventBuilder
{
    public static MagpieEvent BuildSubjectCreatedEvent(Guid subjectId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.SubjectId] = subjectId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.SubjectCreated, context, default, default, null);
    }

    public static MagpieEvent BuildSubjectDeletedEvent(Guid subjectId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.SubjectId] = subjectId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.SubjectDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildSubjectFilterCreatedEvent(Guid subjectFilterId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.SubjectFilterId] = subjectFilterId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.SubjectFilterCreated, context, default, default, null);
    }

    public static MagpieEvent BuildSubjectFilterDeletedEvent(Guid subjectFilterId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.SubjectFilterId] = subjectFilterId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.SubjectFilterDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildSubjectFilterUpdatedEvent(
        Guid subjectFilterId,
        IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.SubjectFilterId] = subjectFilterId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.SubjectFilterChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.SubjectFilterUpdated, context, default, default, null);
    }

    public static MagpieEvent BuildSubjectUpdatedEvent(Guid subjectId, IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.SubjectId] = subjectId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.SubjectChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.SubjectUpdated, context, default, default, null);
    }
}
