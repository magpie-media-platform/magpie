using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class NotificationEventBuilder
{
    public static MagpieEvent BuildNotificationRuleCreatedEvent(Guid notificationRuleId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.NotificationRuleId] = notificationRuleId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.NotificationRuleCreated, context, default, default, null);
    }

    public static MagpieEvent BuildNotificationRuleDeletedEvent(Guid notificationRuleId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.NotificationRuleId] = notificationRuleId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.NotificationRuleDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildNotificationRuleUpdatedEvent(
        Guid notificationRuleId,
        IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.NotificationRuleId] = notificationRuleId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.NotificationRuleChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.NotificationRuleUpdated, context, default, default, null);
    }

    public static MagpieEvent BuildNotificationTestEvent(Guid ruleId, string ruleName, bool test)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.RuleId] = ruleId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.RuleName] = ruleName ?? string.Empty,
            [MagpieEvents.Keys.Test] = test ? "true" : "false",
        };

        return new(Guid.Empty, MagpieEvents.Names.NotificationTest, context, default, default, null);
    }
}
