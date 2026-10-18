using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Api.Observability;
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
    internal static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);

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
        MagpieEvents.Names.ApiKeyCreated,
        MagpieEvents.Names.ApiKeyDeleted,
        MagpieEvents.Names.ApiKeyUpdated,
        MagpieEvents.Names.AuditRecordCreated,
        MagpieEvents.Names.BackupCompleted,
        MagpieEvents.Names.BackupDeleted,
        MagpieEvents.Names.BackupFailed,
        MagpieEvents.Names.UserCreated,
        MagpieEvents.Names.UserDeleted,
        MagpieEvents.Names.UserLockedOut,
        MagpieEvents.Names.UserUnlocked,
        MagpieEvents.Names.UserUpdated,
    ];

    private static readonly HashSet<string> ManagerEvents =
    [
        MagpieEvents.Names.SettingsGroupUpdated,
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
        MagpieEvents.Keys.IntegrationType,
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

    private static readonly HashSet<string> AccountLifecycleNames =
    [
        MagpieEvents.Names.UserDeleted,
        MagpieEvents.Names.UserLockedOut,
        MagpieEvents.Names.UserUnlocked,
        MagpieEvents.Names.UserUpdated,
    ];

    private static readonly HashSet<string> SessionRevokingFields = new(StringComparer.OrdinalIgnoreCase)
    {
        MagpieEvents.Fields.UserEnabled,
        MagpieEvents.Fields.UserRole,
    };

    public string Name => "events";

    private static string GetNamePrefix(string name)
    {
        var separator = name.IndexOf('.');
        return separator < 0 ? name : name[..separator];
    }

    private static bool CanViewEvent(MagpieEvent evt, AuthenticatedUser user)
    {
        if (!CoreEventNames.Contains(evt.Name))
        {
            return false;
        }

        var requiredRole = AdminOnlyEvents.Contains(evt.Name)
            ? Role.Admin
            : ManagerEvents.Contains(evt.Name)
                ? Role.Manager
                : RequesterEvents.Contains(evt.Name)
                    ? Role.Requester
                    : Role.Viewer;

        return user.IsInRole(requiredRole) || ConcernsAccountOf(evt, user);
    }

    private static async Task WriteSseFrameAsync(
        HttpResponse response,
        MagpieEvent evt,
        CancellationToken cancellationToken)
    {
        var context = evt.Context.Where(p => SignalContextKeys.Contains(p.Key)).ToDictionary();

        var payload = new { name = evt.Name, context };
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        await WriteRawAsync(response, $"data: {json}\n\n", cancellationToken).ConfigureAwait(false);
    }

    internal async Task StreamEventsAsync(
        HttpContext httpContext,
        [FromServices] IEventBus eventBus,
        [FromServices] IHostApplicationLifetime applicationLifetime,
        [FromServices] TimeProvider timeProvider,
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

        var startedTimestamp = Stopwatch.GetTimestamp();
        var disconnectReason = "client";
        ApiTelemetry.SseActiveStreams.Add(1);

        try
        {
            await response.StartAsync(streamCts.Token).ConfigureAwait(false);

            var listener = eventBus.CreateListener(eventName: null, localOnly: false, callSite: "event-stream");
            await using var _ = listener.ConfigureAwait(false);

            await WriteRawAsync(response, ": connected\n\n", streamCts.Token).ConfigureAwait(false);

            var events = listener.ReadAllAsync(streamCts.Token).GetAsyncEnumerator(streamCts.Token);
            var next = events.MoveNextAsync().AsTask();
            while (true)
            {
                bool hasEvent;
                try
                {
                    hasEvent = await next
                        .WaitAsync(KeepAliveInterval, timeProvider, streamCts.Token)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    await WriteRawAsync(response, ": keepalive\n\n", streamCts.Token).ConfigureAwait(false);
                    continue;
                }

                if (!hasEvent)
                {
                    break;
                }

                var evt = events.Current;
                next = events.MoveNextAsync().AsTask();

                if (RevokesSessionOf(evt, user))
                {
                    disconnectReason = "session_revoked";
                    break;
                }

                if (!CanViewEvent(evt, user))
                {
                    ApiTelemetry.EventsDroppedByAllowlistTotal.Add(
                        1,
                        new KeyValuePair<string, object?>("name_prefix", GetNamePrefix(evt.Name)));
                    continue;
                }

                await WriteSseFrameAsync(response, evt, streamCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (streamCts.IsCancellationRequested)
        {
            disconnectReason = applicationLifetime.ApplicationStopping.IsCancellationRequested
                ? "server_shutdown"
                : "client";
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Event stream connection closed during write");
        }
        catch (Exception ex)
        {
            disconnectReason = "error";
            logger.LogWarning(ex, "Event stream writer failed");
        }
        finally
        {
            ApiTelemetry.SseActiveStreams.Add(-1);
            ApiTelemetry.SseStreamDuration.Record(
                Stopwatch.GetElapsedTime(startedTimestamp).TotalSeconds,
                new KeyValuePair<string, object?>("disconnect_reason", disconnectReason));
        }
    }

    private static bool ConcernsAccountOf(MagpieEvent evt, AuthenticatedUser user)
    {
        return AccountLifecycleNames.Contains(evt.Name) && IdentifiesUser(evt, user);
    }

    private static bool RevokesSessionOf(MagpieEvent evt, AuthenticatedUser user)
    {
        return string.Equals(evt.Name, MagpieEvents.Names.UserUpdated, StringComparison.Ordinal) &&
            IdentifiesUser(evt, user) &&
            evt.Context.TryGetValue(MagpieEvents.Keys.UserChangedFields, out var changedFields) &&
            changedFields.Split(',').Any(field => SessionRevokingFields.Contains(field.Trim()));
    }

    private static bool IdentifiesUser(MagpieEvent evt, AuthenticatedUser user)
    {
        return evt.Context.TryGetValue(MagpieEvents.Keys.UserId, out var userId) &&
            Guid.TryParse(userId, out var parsed) &&
            parsed == user.Id;
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
