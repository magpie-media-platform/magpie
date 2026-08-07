using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class TriggerEventBuilder
{
    public static MagpieEvent BuildEventTriggerCreatedEvent(Guid eventTriggerId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.EventTriggerId] = eventTriggerId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.EventTriggerCreated, context, default, default, null);
    }

    public static MagpieEvent BuildEventTriggerDeletedEvent(Guid eventTriggerId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.EventTriggerId] = eventTriggerId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.EventTriggerDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildEventTriggerUpdatedEvent(
        Guid eventTriggerId,
        IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.EventTriggerId] = eventTriggerId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.EventTriggerChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.EventTriggerUpdated, context, default, default, null);
    }

    public static MagpieEvent BuildScheduleTriggerCreatedEvent(Guid scheduleTriggerId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ScheduleTriggerId] = scheduleTriggerId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.ScheduleTriggerCreated, context, default, default, null);
    }

    public static MagpieEvent BuildScheduleTriggerDeletedEvent(Guid scheduleTriggerId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ScheduleTriggerId] = scheduleTriggerId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.ScheduleTriggerDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildScheduleTriggerUpdatedEvent(
        Guid scheduleTriggerId,
        IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ScheduleTriggerId] = scheduleTriggerId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.ScheduleTriggerChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.ScheduleTriggerUpdated, context, default, default, null);
    }
}
