using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Api;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.Api.EndpointGroups;

internal sealed class EventStreamEndpointGroup : IApiEndpointGroup
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly HashSet<string> CoreEventNames =
    [
        MagpieEvents.Names.ApiKeyCreated,
        MagpieEvents.Names.ApiKeyDeleted,
        MagpieEvents.Names.ApiKeyUpdated,
        MagpieEvents.Names.AuditRecordCreated,
        MagpieEvents.Names.BackupCompleted,
        MagpieEvents.Names.BackupDeleted,
        MagpieEvents.Names.BackupFailed,
        MagpieEvents.Names.BlockedFileTypeCreated,
        MagpieEvents.Names.BlockedFileTypeDeleted,
        MagpieEvents.Names.ContentFilterCreated,
        MagpieEvents.Names.ContentFilterDeleted,
        MagpieEvents.Names.ContentFilterUpdated,
        MagpieEvents.Names.ContentFulfillmentChanged,
        MagpieEvents.Names.ContentHierarchyItemCreated,
        MagpieEvents.Names.ContentHierarchyItemDeleted,
        MagpieEvents.Names.ContentHierarchyItemUpdated,
        MagpieEvents.Names.DiagnosticFindingRaised,
        MagpieEvents.Names.DiagnosticFindingUpdated,
        MagpieEvents.Names.DiagnosticScanCompleted,
        MagpieEvents.Names.DownloadClientCreated,
        MagpieEvents.Names.DownloadClientDeleted,
        MagpieEvents.Names.DownloadClientUpdated,
        MagpieEvents.Names.DownloadCompleted,
        MagpieEvents.Names.DownloadCreated,
        MagpieEvents.Names.DownloadDeleted,
        MagpieEvents.Names.DownloadFailed,
        MagpieEvents.Names.DownloadImported,
        MagpieEvents.Names.DownloadStalled,
        MagpieEvents.Names.DownloadUpdated,
        MagpieEvents.Names.EventTriggerCreated,
        MagpieEvents.Names.EventTriggerDeleted,
        MagpieEvents.Names.EventTriggerUpdated,
        MagpieEvents.Names.ExtensionDisabled,
        MagpieEvents.Names.ExtensionEnabled,
        MagpieEvents.Names.ExtensionInstalled,
        MagpieEvents.Names.ExtensionPackageSourceCreated,
        MagpieEvents.Names.ExtensionPackageSourceDeleted,
        MagpieEvents.Names.ExtensionPackageSourceUpdated,
        MagpieEvents.Names.ExtensionUninstalled,
        MagpieEvents.Names.ExtensionUpdated,
        MagpieEvents.Names.ExtensionUpgraded,
        MagpieEvents.Names.IndexerCreated,
        MagpieEvents.Names.IndexerDeleted,
        MagpieEvents.Names.IndexerUpdated,
        MagpieEvents.Names.IndexerFeedCreated,
        MagpieEvents.Names.IndexerFeedDeleted,
        MagpieEvents.Names.IndexerFeedUpdated,
        MagpieEvents.Names.IntegrationHealthChanged,
        MagpieEvents.Names.JobExecutionCancellationRequested,
        MagpieEvents.Names.JobExecutionCancelled,
        MagpieEvents.Names.JobExecutionCompleted,
        MagpieEvents.Names.JobExecutionCreated,
        MagpieEvents.Names.JobExecutionFailed,
        MagpieEvents.Names.JobExecutionProgressChanged,
        MagpieEvents.Names.JobExecutionQueued,
        MagpieEvents.Names.JobExecutionStarted,
        MagpieEvents.Names.LibraryImportCandidateUpdated,
        MagpieEvents.Names.LibraryScanCompleted,
        MagpieEvents.Names.MediaServerCreated,
        MagpieEvents.Names.MediaServerDeleted,
        MagpieEvents.Names.MediaServerUpdated,
        MagpieEvents.Names.MetadataRefreshed,
        MagpieEvents.Names.MimeMappingCreated,
        MagpieEvents.Names.MimeMappingDeleted,
        MagpieEvents.Names.MimeMappingUpdated,
        MagpieEvents.Names.NotificationRuleCreated,
        MagpieEvents.Names.NotificationRuleDeleted,
        MagpieEvents.Names.NotificationRuleUpdated,
        MagpieEvents.Names.NotificationTest,
        MagpieEvents.Names.QualityTierListCreated,
        MagpieEvents.Names.QualityTierListDeleted,
        MagpieEvents.Names.QualityTierListUpdated,
        MagpieEvents.Names.ReleaseGrabRejected,
        MagpieEvents.Names.ReleaseSearchCompleted,
        MagpieEvents.Names.RemotePathMappingCreated,
        MagpieEvents.Names.RemotePathMappingDeleted,
        MagpieEvents.Names.RemotePathMappingUpdated,
        MagpieEvents.Names.RootFolderCreated,
        MagpieEvents.Names.RootFolderDeleted,
        MagpieEvents.Names.RootFolderUpdated,
        MagpieEvents.Names.ScheduleTriggerCreated,
        MagpieEvents.Names.ScheduleTriggerDeleted,
        MagpieEvents.Names.ScheduleTriggerUpdated,
        MagpieEvents.Names.SettingsGroupUpdated,
        MagpieEvents.Names.SubjectCreated,
        MagpieEvents.Names.SubjectDeleted,
        MagpieEvents.Names.SubjectUpdated,
        MagpieEvents.Names.SubjectFilterCreated,
        MagpieEvents.Names.SubjectFilterDeleted,
        MagpieEvents.Names.SubjectFilterUpdated,
        MagpieEvents.Names.UserCreated,
        MagpieEvents.Names.UserDeleted,
        MagpieEvents.Names.UserLockedOut,
        MagpieEvents.Names.UserUnlocked,
        MagpieEvents.Names.UserUpdated,
        MagpieEvents.Names.WishlistCreated,
        MagpieEvents.Names.WishlistDeleted,
        MagpieEvents.Names.WishlistItemMatched,
        MagpieEvents.Names.WishlistUpdated,
    ];

    private static readonly HashSet<string> AdminOnlyEvents =
    [
        MagpieEvents.Names.BackupCompleted,
        MagpieEvents.Names.BackupDeleted,
        MagpieEvents.Names.BackupFailed,
    ];

    private static readonly HashSet<string> RequesterEvents =
    [
        MagpieEvents.Names.NotificationRuleCreated,
        MagpieEvents.Names.NotificationRuleDeleted,
        MagpieEvents.Names.NotificationRuleUpdated,
        MagpieEvents.Names.NotificationTest,
    ];

    private static readonly HashSet<string> SignalContextKeys =
    [
        MagpieEvents.Keys.ApiKeyId,
        MagpieEvents.Keys.AuditRecordId,
        MagpieEvents.Keys.BackupId,
        MagpieEvents.Keys.BlockedFileTypeId,
        MagpieEvents.Keys.ContentFilterId,
        MagpieEvents.Keys.ContentHierarchyItemId,
        MagpieEvents.Keys.ContentImpactMetadataContainerId,
        MagpieEvents.Keys.ContentImpactParentId,
        MagpieEvents.Keys.ContentImpactAncestorIds,
        MagpieEvents.Keys.ContentImpactRootId,
        MagpieEvents.Keys.ContentImpactContentType,
        MagpieEvents.Keys.ContentImpactLevel,
        MagpieEvents.Keys.ContentImpactTopLevel,
        MagpieEvents.Keys.ContentImpactRootFolderId,
        MagpieEvents.Keys.ContentImpactQualityTierListId,
        MagpieEvents.Keys.ContentImpactMonitoring,
        MagpieEvents.Keys.ContentImpactFulfillment,
        MagpieEvents.Keys.ContentImpactWishlistId,
        MagpieEvents.Keys.ContentImpactSubtreeIds,
        MagpieEvents.Keys.ContentImpactSubtreeTruncated,
        MagpieEvents.Keys.ContentImpactBatchIds,
        MagpieEvents.Keys.ContentImpactBatchTruncated,
        MagpieEvents.Keys.ContentImpactMetadataSourceContainerId,
        MagpieEvents.Keys.ContentImpactMetadataAddedContainerIds,
        MagpieEvents.Keys.ContentImpactMetadataRemovedContainerIds,
        MagpieEvents.Keys.ContentImpactPreviousParentId,
        MagpieEvents.Keys.ContentImpactPreviousAncestorIds,
        MagpieEvents.Keys.ContentImpactPreviousRootId,
        MagpieEvents.Keys.ContentImpactPreviousContentType,
        MagpieEvents.Keys.ContentImpactPreviousLevel,
        MagpieEvents.Keys.ContentImpactPreviousTopLevel,
        MagpieEvents.Keys.ContentImpactPreviousRootFolderId,
        MagpieEvents.Keys.ContentImpactPreviousQualityTierListId,
        MagpieEvents.Keys.ContentImpactPreviousMonitoring,
        MagpieEvents.Keys.ContentImpactPreviousFulfillment,
        MagpieEvents.Keys.ContentItemId,
        MagpieEvents.Keys.DiagnosticFindingId,
        MagpieEvents.Keys.DownloadClientId,
        MagpieEvents.Keys.DownloadId,
        MagpieEvents.Keys.DownloadIndexerId,
        MagpieEvents.Keys.DownloadReleaseId,
        MagpieEvents.Keys.EventTriggerId,
        MagpieEvents.Keys.ExecutionId,
        MagpieEvents.Keys.ExecutionJobType,
        MagpieEvents.Keys.ExtensionId,
        MagpieEvents.Keys.ExtensionPackageSourceId,
        MagpieEvents.Keys.IndexerFeedId,
        MagpieEvents.Keys.IndexerId,
        MagpieEvents.Keys.IntegrationId,
        MagpieEvents.Keys.LibraryImportCandidateId,
        MagpieEvents.Keys.MediaServerId,
        MagpieEvents.Keys.MimeMappingExtension,
        MagpieEvents.Keys.NotificationRuleId,
        MagpieEvents.Keys.QualityTierListId,
        MagpieEvents.Keys.ReleaseId,
        MagpieEvents.Keys.RemotePathMappingId,
        MagpieEvents.Keys.RootFolderId,
        MagpieEvents.Keys.RuleId,
        MagpieEvents.Keys.ScheduleTriggerId,
        MagpieEvents.Keys.SettingsGroupExtensionId,
        MagpieEvents.Keys.SettingsGroupName,
        MagpieEvents.Keys.SubjectFilterId,
        MagpieEvents.Keys.SubjectId,
        MagpieEvents.Keys.UserId,
        MagpieEvents.Keys.WishlistId,
        MagpieEvents.Keys.WishlistItemId,
    ];

    public string Name => "events";

    private static bool CanViewEvent(MagpieEvent evt, AuthenticatedUser user)
    {
        if (!CoreEventNames.Contains(evt.Name))
        {
            return false;
        }

        var requiredRole = AdminOnlyEvents.Contains(evt.Name)
            ? Role.Admin
            : RequesterEvents.Contains(evt.Name)
                ? Role.Requester
                : Role.Viewer;

        return user.IsInRole(requiredRole);
    }

    private static async Task WriteSseFrameAsync(
        HttpResponse response,
        MagpieEvent evt,
        CancellationToken cancellationToken)
    {
        var context = new Dictionary<string, string>();
        foreach (var (key, value) in evt.Context)
        {
            if (SignalContextKeys.Contains(key))
            {
                context[key] = value;
            }
        }

        var payload = new
        {
            name = evt.Name,
            context,
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        var sb = new StringBuilder();
        sb.Append("data: ").Append(json).Append("\n\n");

        await WriteRawAsync(response, sb.ToString(), cancellationToken).ConfigureAwait(false);
    }

    internal async Task StreamEventsAsync(
        HttpContext httpContext,
        [FromServices] IEventBus eventBus,
        [FromServices] IHostApplicationLifetime applicationLifetime,
        [FromServices] ILogger<EventStreamEndpointGroup> logger,
        AuthenticatedUser user,
        CancellationToken cancellationToken)
    {
        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            applicationLifetime.ApplicationStopping);

        var response = httpContext.Response;

        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache, no-transform";
        response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await response.StartAsync(streamCts.Token).ConfigureAwait(false);

            var listener = eventBus.CreateListener();
            await using var _ = listener.ConfigureAwait(false);

            await WriteRawAsync(response, ": connected\n\n", streamCts.Token).ConfigureAwait(false);

            await foreach (var evt in listener.ReadAllAsync(streamCts.Token).ConfigureAwait(false))
            {
                if (!CanViewEvent(evt, user))
                {
                    continue;
                }

                await WriteSseFrameAsync(response, evt, streamCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (streamCts.IsCancellationRequested)
        {
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "The event stream connection closed during a write.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The event stream writer failed. The handler will close the connection.");
        }
    }

    private static async Task WriteRawAsync(HttpResponse response, string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await response.Body.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("stream", StreamEventsAsync)
            .WithName(nameof(StreamEventsAsync))
            .RequireViewer();
    }
}
