using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class ContentEventBuilder
{
    public static MagpieEvent BuildContentFulfillmentChangedEvent(
        ContentEventImpact impact,
        FulfillmentStatus from,
        FulfillmentStatus to)
    {
        var context = impact.ToContext();
        context[MagpieEvents.Keys.ContentFulfillmentFrom] = from.ToString();
        context[MagpieEvents.Keys.ContentFulfillmentTo] = to.ToString();

        return new(Guid.Empty, MagpieEvents.Names.ContentFulfillmentChanged, context, default, default, null);
    }

    public static MagpieEvent BuildContentHierarchyItemCreatedEvent(ContentEventImpact impact)
    {
        return new(
            Guid.Empty,
            MagpieEvents.Names.ContentHierarchyItemCreated,
            impact.ToContext(),
            default,
            default,
            null);
    }

    public static MagpieEvent BuildContentHierarchyItemDeletedEvent(ContentEventImpact impact)
    {
        return new(
            Guid.Empty,
            MagpieEvents.Names.ContentHierarchyItemDeleted,
            impact.ToContext(),
            default,
            default,
            null);
    }

    public static MagpieEvent BuildContentHierarchyItemUpdatedEvent(
        ContentEventImpact impact,
        IReadOnlyCollection<string> changedFields)
    {
        var context = impact.ToContext();
        context[MagpieEvents.Keys.ContentChangedFields] = string.Join(",", changedFields ?? []);

        return new(Guid.Empty, MagpieEvents.Names.ContentHierarchyItemUpdated, context, default, default, null);
    }

    public static MagpieEvent BuildContentImportedEvent(ContentEventImpact impact, Guid? userId, int importedFileCount)
    {
        var context = impact.ToContext();
        context[MagpieEvents.Keys.ContentImportedFileCount] = importedFileCount.ToString(CultureInfo.InvariantCulture);

        if (userId is { } acting)
        {
            context[MagpieEvents.Keys.UserId] = acting.ToString("D", CultureInfo.InvariantCulture);
        }

        return new(Guid.Empty, MagpieEvents.Names.ContentImported, context, default, default, null);
    }

    public static MagpieEvent BuildLibraryImportCandidateUpdatedEvent(Guid libraryImportCandidateId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.LibraryImportCandidateId] =
                libraryImportCandidateId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.LibraryImportCandidateUpdated, context, default, default, null);
    }

    public static MagpieEvent BuildLibraryScanCompletedEvent(Guid rootFolderId, string summary)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.RootFolderId] = rootFolderId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.ScanSummary] = summary ?? string.Empty,
        };

        return new(Guid.Empty, MagpieEvents.Names.LibraryScanCompleted, context, default, default, null);
    }

    public static MagpieEvent BuildMetadataRefreshedEvent(ContentEventImpact impact)
    {
        return new(Guid.Empty, MagpieEvents.Names.MetadataRefreshed, impact.ToContext(), default, default, null);
    }
}
