using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Magpie.Models;

/// <summary>
/// Extension methods that convert events to and from job trigger context.
/// </summary>
public static class MagpieEventExtensions
{
    private const string EventContextPrefix = "event.context.";

    /// <summary>
    /// Gets the trigger-context key for an event context entry.
    /// </summary>
    /// <param name="contextKey">The name of the event context entry</param>
    public static string ToEventContextKey(this string contextKey)
    {
        return EventContextPrefix + contextKey;
    }

    /// <summary>
    /// Converts the event into a trigger-context dictionary.
    /// </summary>
    /// <param name="magpieEvent">The event to convert</param>
    public static Dictionary<string, string> ToTriggerContext(this MagpieEvent magpieEvent)
    {
        var context = new Dictionary<string, string>
        {
            ["event.id"] = magpieEvent.Id.ToString("D"),
            ["event.name"] = magpieEvent.Name,
            ["event.occurredAt"] = magpieEvent.OccurredAt.ToString("O"),
            ["event.publishedAt"] = magpieEvent.PublishedAt.ToString("O")
        };

        if (!string.IsNullOrWhiteSpace(magpieEvent.NodeId))
        {
            context["event.nodeId"] = magpieEvent.NodeId;
        }

        if (magpieEvent.JobExecutionId is { } jobExecutionId)
        {
            context["event.jobExecutionId"] = jobExecutionId.ToString("D");
        }

        foreach (var (key, value) in magpieEvent.Context)
        {
            context[key.ToEventContextKey()] = value;
        }

        return context;
    }

    /// <summary>
    /// Converts a trigger-context dictionary into an event. If the dictionary holds no event, returns null.
    /// </summary>
    /// <param name="triggerContext">The trigger-context dictionary to convert</param>
    public static MagpieEvent? ToMagpieEvent(this IReadOnlyDictionary<string, string> triggerContext)
    {
        if (!triggerContext.TryGetValue("event.id", out var idRaw) ||
            !Guid.TryParse(idRaw, out var id) ||
            !triggerContext.TryGetValue("event.name", out var name) ||
            !triggerContext.TryGetValue("event.occurredAt", out var occurredAtRaw) ||
            !DateTime.TryParse(occurredAtRaw, null, DateTimeStyles.RoundtripKind, out var occurredAt) ||
            !triggerContext.TryGetValue("event.publishedAt", out var publishedAtRaw) ||
            !DateTime.TryParse(publishedAtRaw, null, DateTimeStyles.RoundtripKind, out var publishedAt))
        {
            return null;
        }

        var context = triggerContext
            .Where(kvp => kvp.Key.StartsWith(EventContextPrefix, StringComparison.Ordinal))
            .ToDictionary(
                kvp => kvp.Key[EventContextPrefix.Length..],
                kvp => kvp.Value,
                StringComparer.Ordinal);

        triggerContext.TryGetValue("event.nodeId", out var nodeId);

        var jobExecutionId = triggerContext.TryGetValue("event.jobExecutionId", out var jobExecutionIdRaw) &&
            Guid.TryParse(jobExecutionIdRaw, out var parsedJobExecutionId)
                ? parsedJobExecutionId
                : default(Guid?);

        return new(
            id,
            name,
            context,
            occurredAt,
            publishedAt,
            string.IsNullOrWhiteSpace(nodeId) ? null : nodeId)
        {
            JobExecutionId = jobExecutionId
        };
    }
}
