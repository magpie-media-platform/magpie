using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class IntegrationEventBuilder
{
    public static MagpieEvent BuildIntegrationHealthChangedEvent(
        Guid integrationId,
        string type,
        string from,
        string to)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.IntegrationId] = integrationId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.IntegrationType] = type ?? string.Empty,
            [MagpieEvents.Keys.IntegrationFrom] = from ?? string.Empty,
            [MagpieEvents.Keys.IntegrationTo] = to ?? string.Empty,
        };

        return new(Guid.Empty, MagpieEvents.Names.IntegrationHealthChanged, context, default, default, null);
    }

    public static MagpieEvent BuildMediaServerCreatedEvent(Guid mediaServerId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.MediaServerId] = mediaServerId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.MediaServerCreated, context, default, default, null);
    }

    public static MagpieEvent BuildMediaServerDeletedEvent(Guid mediaServerId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.MediaServerId] = mediaServerId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.MediaServerDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildMediaServerUpdatedEvent(
        Guid mediaServerId,
        IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.MediaServerId] = mediaServerId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.MediaServerChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.MediaServerUpdated, context, default, default, null);
    }
}
