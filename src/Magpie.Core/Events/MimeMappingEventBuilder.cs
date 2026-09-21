using System;
using System.Collections.Generic;

using Magpie.Models;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class MimeMappingEventBuilder
{
    public static MagpieEvent BuildMimeMappingCreatedEvent(string extension)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.MimeMappingExtension] = extension,
        };

        return new(Guid.Empty, MagpieEvents.Names.MimeMappingCreated, context, default, default, null);
    }

    public static MagpieEvent BuildMimeMappingDeletedEvent(string extension)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.MimeMappingExtension] = extension,
        };

        return new(Guid.Empty, MagpieEvents.Names.MimeMappingDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildMimeMappingUpdatedEvent(string extension, IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.MimeMappingExtension] = extension,
            [MagpieEvents.Keys.MimeMappingChangedFields] = string.Join(",", changedFields),
        };

        return new(Guid.Empty, MagpieEvents.Names.MimeMappingUpdated, context, default, default, null);
    }
}
