using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class RootFolderEventBuilder
{
    public static MagpieEvent BuildRootFolderCreatedEvent(RootFolder rootFolder)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.RootFolderId] = rootFolder.Id.ToString("D"),
            [MagpieEvents.Keys.RootFolderPath] = rootFolder.Path ?? string.Empty,
            [MagpieEvents.Keys.RootFolderContentType] = rootFolder.ContentType ?? string.Empty,
        };

        return new(Guid.Empty, MagpieEvents.Names.RootFolderCreated, context, default, default, null);
    }

    public static MagpieEvent BuildRootFolderDeletedEvent(Guid rootFolderId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.RootFolderId] = rootFolderId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.RootFolderDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildRootFolderUpdatedEvent(Guid rootFolderId, IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.RootFolderId] = rootFolderId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.RootFolderChangedFields] = string.Join(",", changedFields ?? []),
        };
        return new(Guid.Empty, MagpieEvents.Names.RootFolderUpdated, context, default, default, null);
    }
}
