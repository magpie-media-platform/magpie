using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class ReleaseEventBuilder
{
    public static MagpieEvent BuildReleaseGrabRejectedEvent(
        Guid releaseId,
        Guid indexerId,
        string rejectionReason,
        ContentEventImpact? impact)
    {
        var context = impact?.ToContext() ?? [];
        context[MagpieEvents.Keys.ReleaseId] = releaseId.ToString("D", CultureInfo.InvariantCulture);
        context[MagpieEvents.Keys.IndexerId] = indexerId.ToString("D", CultureInfo.InvariantCulture);
        context[MagpieEvents.Keys.ReleaseRejectionReason] = rejectionReason ?? string.Empty;

        return new(Guid.Empty, MagpieEvents.Names.ReleaseGrabRejected, context, default, default, null);
    }

    public static MagpieEvent BuildReleaseMatchedEvent(ContentEventImpact impact)
    {
        return new(Guid.Empty, MagpieEvents.Names.ReleaseMatched, impact.ToContext(), default, default, null);
    }

    public static MagpieEvent BuildReleaseScopeMatchedEvent(IReadOnlyList<Guid> hierarchyItemIds)
    {
        var context = new ContentEventImpact { BatchIds = hierarchyItemIds }.ToContext();

        return new(Guid.Empty, MagpieEvents.Names.ReleaseScopeMatched, context, default, default, null);
    }

    public static MagpieEvent BuildReleaseSearchCompletedEvent(ContentEventImpact impact, int count)
    {
        var context = impact.ToContext();
        context[MagpieEvents.Keys.ReleaseSearchCount] = count.ToString(CultureInfo.InvariantCulture);

        return new(Guid.Empty, MagpieEvents.Names.ReleaseSearchCompleted, context, default, default, null);
    }
}
