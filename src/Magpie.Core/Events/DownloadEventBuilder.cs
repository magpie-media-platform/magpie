using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class DownloadEventBuilder
{
    public static MagpieEvent BuildDownloadClientCreatedEvent(Guid downloadClientId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.DownloadClientId] = downloadClientId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.DownloadClientCreated, context, default, default, null);
    }

    public static MagpieEvent BuildDownloadClientDeletedEvent(Guid downloadClientId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.DownloadClientId] = downloadClientId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.DownloadClientDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildDownloadClientUpdatedEvent(
        Guid downloadClientId,
        IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.DownloadClientId] = downloadClientId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.DownloadClientChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.DownloadClientUpdated, context, default, default, null);
    }

    public static MagpieEvent BuildDownloadCompletedEvent(Guid downloadId, Guid? releaseId, ContentEventImpact? impact)
    {
        return new(
            Guid.Empty,
            MagpieEvents.Names.DownloadCompleted,
            DownloadContext(downloadId, releaseId, impact),
            default,
            default,
            null);
    }

    public static MagpieEvent BuildDownloadCreatedEvent(Guid downloadId, Guid? releaseId, ContentEventImpact? impact)
    {
        return new(
            Guid.Empty,
            MagpieEvents.Names.DownloadCreated,
            DownloadContext(downloadId, releaseId, impact),
            default,
            default,
            null);
    }

    public static MagpieEvent BuildDownloadDeletedEvent(Guid downloadId, ContentEventImpact? impact)
    {
        return new(
            Guid.Empty,
            MagpieEvents.Names.DownloadDeleted,
            DownloadContext(downloadId, releaseId: null, impact),
            default,
            default,
            null);
    }

    public static MagpieEvent BuildDownloadFailedEvent(
        Guid downloadId,
        Guid? releaseId,
        DownloadFailureReason failureReason,
        ContentEventImpact? impact,
        Guid? indexerId = null)
    {
        var context = DownloadContext(downloadId, releaseId, impact);
        context[MagpieEvents.Keys.DownloadFailureReason] = failureReason.ToString();

        if (indexerId.HasValue)
        {
            context[MagpieEvents.Keys.DownloadIndexerId] = indexerId.Value.ToString("D", CultureInfo.InvariantCulture);
        }

        return new(Guid.Empty, MagpieEvents.Names.DownloadFailed, context, default, default, null);
    }

    public static MagpieEvent BuildDownloadImportedEvent(
        Guid downloadId,
        Guid? releaseId,
        ContentEventImpact? impact,
        Guid? indexerId,
        int importedFileCount)
    {
        var context = DownloadContext(downloadId, releaseId, impact);
        context[MagpieEvents.Keys.ContentImportedFileCount] = importedFileCount.ToString(CultureInfo.InvariantCulture);

        if (indexerId.HasValue)
        {
            context[MagpieEvents.Keys.DownloadIndexerId] = indexerId.Value.ToString("D", CultureInfo.InvariantCulture);
        }

        return new(Guid.Empty, MagpieEvents.Names.DownloadImported, context, default, default, null);
    }

    public static MagpieEvent BuildDownloadStalledEvent(
        Guid downloadId,
        Guid? releaseId,
        string stallReason,
        ContentEventImpact? impact)
    {
        var context = DownloadContext(downloadId, releaseId, impact);
        context[MagpieEvents.Keys.DownloadStallReason] = stallReason ?? string.Empty;

        return new(Guid.Empty, MagpieEvents.Names.DownloadStalled, context, default, default, null);
    }

    public static MagpieEvent BuildDownloadUpdatedEvent(
        Guid downloadId,
        IReadOnlyCollection<string> changedFields,
        ContentEventImpact? impact)
    {
        var context = DownloadContext(downloadId, releaseId: null, impact);
        context[MagpieEvents.Keys.DownloadChangedFields] = string.Join(",", changedFields ?? []);

        return new(Guid.Empty, MagpieEvents.Names.DownloadUpdated, context, default, default, null);
    }

    public static MagpieEvent BuildRemotePathMappingCreatedEvent(Guid remotePathMappingId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.RemotePathMappingId] = remotePathMappingId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.RemotePathMappingCreated, context, default, default, null);
    }

    public static MagpieEvent BuildRemotePathMappingDeletedEvent(Guid remotePathMappingId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.RemotePathMappingId] = remotePathMappingId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.RemotePathMappingDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildRemotePathMappingUpdatedEvent(
        Guid remotePathMappingId,
        IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.RemotePathMappingId] = remotePathMappingId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.RemotePathMappingChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.RemotePathMappingUpdated, context, default, default, null);
    }

    private static Dictionary<string, string> DownloadContext(
        Guid downloadId,
        Guid? releaseId,
        ContentEventImpact? impact)
    {
        var context = impact?.ToContext() ?? [];
        context[MagpieEvents.Keys.DownloadId] = downloadId.ToString("D", CultureInfo.InvariantCulture);

        if (releaseId.HasValue)
        {
            context[MagpieEvents.Keys.DownloadReleaseId] = releaseId.Value.ToString("D", CultureInfo.InvariantCulture);
        }

        return context;
    }
}
