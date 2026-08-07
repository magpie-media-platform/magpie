using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class IndexerEventBuilder
{
    public static MagpieEvent BuildIndexerCreatedEvent(Guid indexerId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.IndexerId] = indexerId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.IndexerCreated, context, default, default, null);
    }

    public static MagpieEvent BuildIndexerDeletedEvent(Guid indexerId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.IndexerId] = indexerId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.IndexerDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildIndexerFeedCreatedEvent(Guid indexerFeedId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.IndexerFeedId] = indexerFeedId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.IndexerFeedCreated, context, default, default, null);
    }

    public static MagpieEvent BuildIndexerFeedDeletedEvent(Guid indexerFeedId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.IndexerFeedId] = indexerFeedId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.IndexerFeedDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildIndexerFeedUpdatedEvent(
        Guid indexerFeedId,
        IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.IndexerFeedId] = indexerFeedId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.IndexerFeedChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.IndexerFeedUpdated, context, default, default, null);
    }

    public static MagpieEvent BuildIndexerUpdatedEvent(Guid indexerId, IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.IndexerId] = indexerId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.IndexerChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.IndexerUpdated, context, default, default, null);
    }
}
