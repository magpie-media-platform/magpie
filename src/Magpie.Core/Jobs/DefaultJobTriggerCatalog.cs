using System;
using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Sdk.Events;

namespace Magpie.Core.Jobs;

/// <summary>
/// The catalog of built-in triggers for the registered job specs.
/// </summary>
public static class DefaultJobTriggerCatalog
{
    /// <summary>
    /// The built-in schedule triggers.
    /// </summary>
    public static IReadOnlyList<ScheduleTrigger> ScheduleTriggers
    {
        get
        {
            var now = DateTime.UtcNow;

            return
            [
                new()
                {
                    JobType = MagpieJobs.Types.DownloadRefresh,
                    Scope = JobScope.Administrative,
                    Name = "Download refresh schedule",
                    Schedule = "*/5 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.ReleaseAutoGrabScan,
                    Scope = JobScope.Content,
                    Name = "Release auto-grab scan schedule",
                    Schedule = "0 */6 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.ReleaseSearchScan,
                    Scope = JobScope.Content,
                    Name = "Release search scan schedule",
                    Schedule = "*/30 * * * *",
                    Parameters = new Dictionary<string, string>
                    {
                        [MagpieJobs.Parameters.LeadTimeDays] = "1"
                    },
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.RetentionCleanup,
                    Scope = JobScope.Administrative,
                    Name = "Retention cleanup schedule",
                    Schedule = "0 3 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.DatabaseMaintenance,
                    Scope = JobScope.Administrative,
                    Name = "Database maintenance schedule",
                    Schedule = "0 1 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.IndexerFeedSyncScan,
                    Scope = JobScope.Administrative,
                    Name = "Indexer feed sync scan schedule",
                    Schedule = "0 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.IntegrationHealthCheck,
                    Scope = JobScope.Administrative,
                    Name = "Integration health check schedule",
                    Schedule = "0 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.WishlistRefreshScan,
                    Scope = JobScope.Administrative,
                    Name = "Wishlist refresh scan schedule",
                    Schedule = "0 6 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.DownloadImportScan,
                    Scope = JobScope.Administrative,
                    Name = "Download import scan schedule",
                    Schedule = "0 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.DownloadStuckDetection,
                    Scope = JobScope.Administrative,
                    Name = "Download stuck detection schedule",
                    Schedule = "*/5 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.DownloadCleanup,
                    Scope = JobScope.Administrative,
                    Name = "Download cleanup schedule",
                    Schedule = "*/30 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.SubjectMetadataRefreshScan,
                    Scope = JobScope.Administrative,
                    Name = "Subject metadata refresh scan schedule",
                    Schedule = "0 4 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.DiagnosticCheck,
                    Scope = JobScope.System,
                    Name = "Diagnostic check schedule",
                    Schedule = "0 5 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.ContentMetadataRefreshScan,
                    Scope = JobScope.Administrative,
                    Name = "Content metadata refresh scan schedule",
                    Schedule = "0 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.MediaInfoExtractScan,
                    Scope = JobScope.Administrative,
                    Name = "Media info extract scan schedule",
                    Schedule = "0 2 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.MediaFileReconciliation,
                    Scope = JobScope.Administrative,
                    Name = "Media file reconciliation schedule",
                    Schedule = "0 3 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.MetadataContainerCleanup,
                    Scope = JobScope.Administrative,
                    Name = "Metadata container cleanup schedule",
                    Schedule = "30 2 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.SubjectMaintenance,
                    Scope = JobScope.Administrative,
                    Name = "Subject maintenance schedule",
                    Schedule = "15 2 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.RootFolderContentDiscovery,
                    Scope = JobScope.Content,
                    Name = "Root folder content discovery schedule",
                    Schedule = "30 4 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.ContentFulfillmentReconciliation,
                    Scope = JobScope.Administrative,
                    Name = "Content fulfillment reconciliation schedule",
                    Schedule = "0 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
            ];
        }
    }

    /// <summary>
    /// The built-in event triggers.
    /// </summary>
    public static IReadOnlyList<EventTrigger> EventTriggers
    {
        get
        {
            var now = DateTime.UtcNow;

            return
            [
                new()
                {
                    JobType = MagpieJobs.Types.MediaInfoExtractScan,
                    Scope = JobScope.Content,
                    Name = "Media info extract on import",
                    EventFilter = SingleEventFilter(MagpieEvents.Names.ContentImported),
                    Parameters = new Dictionary<string, string>
                    {
                        [MagpieJobs.Parameters.ContentHierarchyItemId] =
                            $"${{event.{MagpieEvents.Keys.ContentHierarchyItemId}}}",
                        [MagpieJobs.Parameters.MediaInfoPolicy] = MagpieJobs.ParameterValues.MissingMediaInfoPolicy
                    },
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.MediaServerRefresh,
                    Scope = JobScope.Administrative,
                    Name = "Media server refresh on download import",
                    EventFilter = SingleEventFilter(MagpieEvents.Names.DownloadImported),
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.DownloadFulfilledCancellation,
                    Scope = JobScope.Administrative,
                    Name = "Cancel fulfilled downloads on download import",
                    EventFilter = SingleEventFilter(MagpieEvents.Names.DownloadImported),
                    Parameters = new Dictionary<string, string>
                    {
                        [MagpieJobs.Parameters.DownloadId] = $"${{event.{MagpieEvents.Keys.DownloadId}}}"
                    },
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.DownloadCleanup,
                    Scope = JobScope.Administrative,
                    Name = "Download cleanup on conclusion",
                    EventFilter = ManyEventFilter(
                        MagpieEvents.Names.DownloadImported,
                        MagpieEvents.Names.DownloadFailed),
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.ContentMetadataRefreshItem,
                    Scope = JobScope.Content,
                    Name = "Content metadata refresh on creation",
                    EventFilter = SingleEventFilter(MagpieEvents.Names.ContentHierarchyItemCreated),
                    Parameters = new Dictionary<string, string>
                    {
                        [MagpieJobs.Parameters.ContentHierarchyItemId] =
                            $"${{event.{MagpieEvents.Keys.ContentHierarchyItemId}}}"
                    },
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.ReleaseSearchItem,
                    Scope = JobScope.Content,
                    Name = "Release search on metadata refresh",
                    EventFilter = SingleEventFilter(MagpieEvents.Names.MetadataRefreshed),
                    Parameters = new Dictionary<string, string>
                    {
                        [MagpieJobs.Parameters.ContentHierarchyItemId] =
                            $"${{event.{MagpieEvents.Keys.ContentHierarchyItemId}}}"
                    },
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.DownloadRefresh,
                    Scope = JobScope.Administrative,
                    Name = "Download refresh on creation",
                    EventFilter = SingleEventFilter(MagpieEvents.Names.DownloadCreated),
                    DelaySeconds = 15,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.ReleaseAutoGrabScan,
                    Scope = JobScope.Content,
                    Name = "Release auto-grab scan on monitoring change",
                    EventFilter = MonitoringChangedEventFilter(),
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.ContentFulfillmentReconciliation,
                    Scope = JobScope.Administrative,
                    Name = "Content fulfillment reconciliation on quality tier list change",
                    EventFilter = ManyEventFilter(
                        MagpieEvents.Names.QualityTierListCreated,
                        MagpieEvents.Names.QualityTierListUpdated,
                        MagpieEvents.Names.QualityTierListDeleted),
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.DownloadImportItem,
                    Scope = JobScope.Content,
                    Name = "Download import on completion",
                    EventFilter = SingleEventFilter(MagpieEvents.Names.DownloadCompleted),
                    Parameters = new Dictionary<string, string>
                    {
                        [MagpieJobs.Parameters.DownloadId] = $"${{event.{MagpieEvents.Keys.DownloadId}}}"
                    },
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.WishlistRefreshItem,
                    Scope = JobScope.Administrative,
                    Name = "Wishlist refresh on creation",
                    EventFilter = SingleEventFilter(MagpieEvents.Names.WishlistCreated),
                    Parameters = new Dictionary<string, string>
                    {
                        [MagpieJobs.Parameters.WishlistId] = $"${{event.{MagpieEvents.Keys.WishlistId}}}"
                    },
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MagpieJobs.Types.ReleaseAutoGrabItem,
                    Scope = JobScope.Content,
                    Name = "Release auto-grab on match",
                    EventFilter = SingleEventFilter(MagpieEvents.Names.ReleaseMatched),
                    Parameters = new Dictionary<string, string>
                    {
                        [MagpieJobs.Parameters.ContentItemId] = $"${{event.{MagpieEvents.Keys.ContentItemId}}}"
                    },
                    Enabled = true,
                    CreatedAt = now,
                },
            ];
        }
    }

    private static EventFilter SingleEventFilter(string eventName)
    {
        return new EventFilter
        {
            EventName = new PropertyCriterion<string>
            {
                Operator = CriteriaValueOperator.Equals,
                Value = eventName,
            },
        };
    }

    private static EventFilter ManyEventFilter(params string[] eventNames)
    {
        return new EventFilter
        {
            EventName = new PropertyCriterion<string>
            {
                Operator = CriteriaValueOperator.In,
                Values = [.. eventNames],
            },
        };
    }

    private static EventFilter MonitoringChangedEventFilter()
    {
        return new EventFilter
        {
            EventName = new PropertyCriterion<string>
            {
                Operator = CriteriaValueOperator.Equals,
                Value = MagpieEvents.Names.ContentHierarchyItemUpdated,
            },
            EventContext = new DictionaryCriteria<PropertyCriterion<string>>
            {
                Items = new()
                {
                    [MagpieEvents.Keys.ContentChangedFields] = new()
                    {
                        Operator = CriteriaValueOperator.ContainsString,
                        Value = nameof(ContentHierarchyItem.EffectiveMonitoring),
                    },
                },
            },
        };
    }
}
