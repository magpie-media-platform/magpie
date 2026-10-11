using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Content;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Observability;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Events;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class MediaServerRefreshJob(
    IMediaServerRepository mediaServerRepository,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IContentFileService contentFileService,
    IMaterializedMetadataRepository materializedMetadataRepository,
    IEnumerable<IMediaServerIntegration> mediaServerIntegrations,
    ILogger<MediaServerRefreshJob> logger,
    IProgressReporter progressReporter) : IJob
{
    private sealed record RefreshScope(
        string ContentType,
        string ContentPath,
        IReadOnlyDictionary<string, string> ExternalIds);

    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.MediaServerRefresh,
        DisplayName = "Media server refresh",
        Description = "Asks configured media servers to refresh their libraries.",
        Category = "Integrations",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Retry,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        CoalesceWideningFields = [MagpieJobs.Parameters.ContentHierarchyItemId],
        LockPartitionEventContextKeys = new Dictionary<string, string>
        {
            [MagpieJobs.Parameters.ContentHierarchyItemId] = MagpieEvents.Keys.ContentHierarchyItemId
        },
        Parameters =
        [
            new(
                MagpieJobs.Parameters.ContentHierarchyItemId,
                "Optional content hierarchy item id passed through when the job was event-triggered.",
                DataType.String,
                false,
                null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            }
        ],
    };

    private static readonly IReadOnlyDictionary<string, string> EmptyExternalIds
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static Guid? ResolveContentHierarchyItemId(JobContext context)
    {
        if (context.Parameters.TryGetValue(MagpieJobs.Parameters.ContentHierarchyItemId, out var rawParameter) &&
            !string.IsNullOrWhiteSpace(rawParameter))
        {
            if (!Guid.TryParse(rawParameter, out var parsed))
            {
                throw new ArgumentException("The content item ID has an invalid format.");
            }

            return parsed;
        }

        if (context.Event is { Context: var eventContext } &&
            eventContext.TryGetValue(MagpieEvents.Keys.ContentHierarchyItemId, out var rawFromEvent) &&
            !string.IsNullOrWhiteSpace(rawFromEvent))
        {
            if (!Guid.TryParse(rawFromEvent, out var parsed))
            {
                throw new ArgumentException("The job received an invalid content item ID.");
            }

            return parsed;
        }

        return null;
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        var contentHierarchyItemId = ResolveContentHierarchyItemId(context);

        using var activity = MediaServerTelemetry.Source.StartActivity("MediaServerRefresh");
        activity?.SetTag("magpie.content_hierarchy_item_id", contentHierarchyItemId?.ToString());

        progressReporter.Report(0, "The refresh will load the media servers.");

        var integrations = BuildIntegrationMap();
        var enabledServers = (await mediaServerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(s => s.Enabled && s.Status != IntegrationHealthStatus.Unhealthy)
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ThenBy(s => s.Id)
            .ToArray();

        activity?.SetTag("magpie.media_servers.server_count", enabledServers.Length);

        var scope = await ResolveScopeAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);

        for (var i = 0; i < enabledServers.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = enabledServers[i];
            progressReporter.ReportFraction(
                i,
                enabledServers.Length,
                $"The media-server refresh will refresh the libraries on {server.Name}.");

            using var serverActivity = MediaServerTelemetry.Source.StartActivity("ServerRefresh");
            serverActivity?.SetTag("magpie.media_server.provider_type", server.ProviderType);
            serverActivity?.SetTag("magpie.media_server.id", server.Id.ToString());
            serverActivity?.SetTag("magpie.media_server.name", server.Name);

            var startedAt = Stopwatch.GetTimestamp();
            var outcome = "error";

            try
            {
                if (!integrations.TryGetValue(server.ProviderType, out var integration))
                {
                    outcome = "skipped_not_refreshable";
                    logger.LogWarning(
                        "No media server integration exists for provider type {ProviderType}",
                        server.ProviderType);
                    continue;
                }

                if (integration is not IRefreshableMediaServerIntegration refreshableIntegration)
                {
                    outcome = "skipped_not_refreshable";
                    logger.LogDebug(
                        "The integration does not support refresh, so the job skipped media server" +
                        " {MediaServerName}",
                        server.Name);
                    continue;
                }

                if (scope is not null &&
                    server.ContentTypes.Count > 0 &&
                    !server.ContentTypes.Contains(scope.ContentType))
                {
                    outcome = "skipped_content_type";
                    logger.LogDebug(
                        "The job skipped the refresh on media server {MediaServerName} because it is not " +
                        "configured for {ContentType}",
                        server.Name,
                        scope.ContentType);
                    continue;
                }

                if (scope is not null &&
                    integration.SupportedContentTypes.Count > 0 &&
                    !integration.SupportedContentTypes.Contains(scope.ContentType))
                {
                    outcome = "skipped_content_type";
                    logger.LogDebug(
                        "The job skipped the refresh on media server {MediaServerName} because the " +
                        "integration does not support {ContentType}",
                        server.Name,
                        scope.ContentType);
                    continue;
                }

                var refreshed = await refreshableIntegration.RefreshForContentAsync(
                    server,
                    scope?.ExternalIds ?? EmptyExternalIds,
                    scope?.ContentType ?? string.Empty,
                    scope?.ContentPath ?? string.Empty,
                    cancellationToken)
                    .ConfigureAwait(false);

                if (!refreshed)
                {
                    outcome = "not_triggered";
                    logger.LogWarning(
                        "Media server {MediaServerName} did not trigger an upstream refresh",
                        server.Name);
                    continue;
                }

                outcome = "success";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                outcome = string.Empty;
                throw;
            }
            catch (Exception ex)
            {
                serverActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                serverActivity?.AddException(ex);
                serverActivity?.SetTag("error.type", ex.GetType().FullName);
                logger.LogWarning(
                    ex,
                    "The job failed to refresh the libraries on media server {MediaServerName}",
                    server.Name);
            }
            finally
            {
                if (outcome.Length > 0)
                {
                    serverActivity?.SetTag("magpie.media_server.outcome", outcome);
                    var providerTag = new KeyValuePair<string, object?>("provider_type", server.ProviderType);
                    var outcomeTag = new KeyValuePair<string, object?>("outcome", outcome);
                    MediaServerTelemetry.Refreshes.Add(1, providerTag, outcomeTag);
                    MediaServerTelemetry.RefreshDuration.Record(
                        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                        providerTag);
                }
            }
        }

        progressReporter.Report(1, "The media-server refresh completed.");
    }

    private async Task<RefreshScope?> ResolveScopeAsync(
        Guid? contentHierarchyItemId,
        CancellationToken cancellationToken)
    {
        if (contentHierarchyItemId is null)
        {
            return null;
        }

        IReadOnlyList<ContentHierarchyItem> lineage;
        try
        {
            lineage = await contentHierarchyItemRepository
                .GetLineageAsync(contentHierarchyItemId.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The job failed to load content hierarchy item {ContentHierarchyItemId}, so the " +
                "media server refresh will continue without a scope",
                contentHierarchyItemId);
            return null;
        }

        var triggeringItem = lineage.FirstOrDefault();
        if (triggeringItem is null)
        {
            logger.LogDebug(
                "The job did not find content hierarchy item {ContentHierarchyItemId}, so the media server " +
                "refresh will continue without a scope",
                contentHierarchyItemId);
            return null;
        }

        if (string.IsNullOrWhiteSpace(triggeringItem.ContentType))
        {
            return null;
        }

        var effectivePath = string.Empty;
        try
        {
            effectivePath = await contentFileService
                .GetEffectivePathAsync(triggeringItem.Id, cancellationToken)
                .ConfigureAwait(false) ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The job failed to resolve the path for content hierarchy item {ContentHierarchyItemId}, so " +
                "the media server refresh will use an empty content path",
                triggeringItem.Id);
        }

        var externalIds = await ResolveExternalIdsAsync(triggeringItem, cancellationToken).ConfigureAwait(false);

        return new RefreshScope(triggeringItem.ContentType, effectivePath, externalIds);
    }

    private async Task<IReadOnlyDictionary<string, string>> ResolveExternalIdsAsync(
        ContentHierarchyItem triggeringItem,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ExternalId> externalIds;
        try
        {
            var metadata = await materializedMetadataRepository.QueryAsync(
                new() { ContainerIds = [triggeringItem.MetadataContainerId] },
                new() { PageNumber = 1, PageSize = 1 },
                null,
                cancellationToken).ConfigureAwait(false);
            externalIds = metadata.Items.FirstOrDefault()?.ExternalIds?.ToList() ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The job failed to load the materialized metadata for content hierarchy item " +
                "{ContentHierarchyItemId}, so the media server refresh will use no external ids",
                triggeringItem.Id);
            return EmptyExternalIds;
        }

        if (externalIds.Count == 0)
        {
            return EmptyExternalIds;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in externalIds)
        {
            if (string.IsNullOrWhiteSpace(id.Source) || string.IsNullOrWhiteSpace(id.Value))
            {
                continue;
            }

            if (!map.ContainsKey(id.Source))
            {
                map[id.Source] = id.Value;
            }
        }

        return map;
    }

    private Dictionary<string, IMediaServerIntegration> BuildIntegrationMap()
    {
        var map = new Dictionary<string, IMediaServerIntegration>(StringComparer.Ordinal);
        foreach (var group in mediaServerIntegrations
            .Where(integration => !string.IsNullOrWhiteSpace(integration.ProviderType))
            .GroupBy(integration => integration.ProviderType, StringComparer.Ordinal))
        {
            var members = group.ToList();
            var winner = members[^1];
            if (members.Count > 1)
            {
                logger.LogWarning(
                    "Multiple media-server integrations declared provider type {ProviderType}, so the job will " +
                    "use {Winner} and ignore {Displaced}",
                    group.Key,
                    winner.GetType().FullName,
                    string.Join(", ", members.Take(members.Count - 1).Select(member => member.GetType().FullName)));
            }

            map[group.Key] = winner;
        }

        return map;
    }
}
