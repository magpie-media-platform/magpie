using System;
using System.Collections.Generic;

using Magpie.Core.Jobs.Downloads;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Core.Jobs.Metadata;
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
                    JobType = DownloadRefreshJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Download refresh schedule",
                    Schedule = "*/5 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = ReleaseAutoGrabScanJob.Spec.JobType,
                    Scope = JobScope.Content,
                    Name = "Release auto-grab scan schedule",
                    Schedule = "0 */6 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = ReleaseSearchScanJob.Spec.JobType,
                    Scope = JobScope.Content,
                    Name = "Release search scan schedule",
                    Schedule = "*/30 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = RetentionCleanupJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Retention cleanup schedule",
                    Schedule = "0 3 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = DatabaseMaintenanceJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Database maintenance schedule",
                    Schedule = "0 1 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = IndexerFeedSyncScanJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Indexer feed sync scan schedule",
                    Schedule = "0 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = IntegrationHealthCheckJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Integration health check schedule",
                    Schedule = "0 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = WishlistRefreshScanJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Wishlist refresh scan schedule",
                    Schedule = "0 6 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = DownloadImportScanJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Download import scan schedule",
                    Schedule = "0 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = DownloadStuckDetectionJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Download stuck detection schedule",
                    Schedule = "*/5 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = DownloadCleanupJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Download cleanup schedule",
                    Schedule = "*/30 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = SubjectMetadataRefreshScanJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Subject metadata refresh scan schedule",
                    Schedule = "0 4 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = DiagnosticCheckJob.Spec.JobType,
                    Scope = JobScope.System,
                    Name = "Diagnostic check schedule",
                    Schedule = "0 5 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = ContentMetadataRefreshScanJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Content metadata refresh scan schedule",
                    Schedule = "0 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MediaInfoExtractScanJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Media info extract scan schedule",
                    Schedule = "0 2 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MetadataContainerCleanupJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Metadata container cleanup schedule",
                    Schedule = "30 2 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = PruneOrphanedSubjectsJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Orphaned subject pruning schedule",
                    Schedule = "15 2 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = ContentFulfillmentReconciliationJob.Spec.JobType,
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
                    JobType = MediaInfoExtractScanJob.Spec.JobType,
                    Scope = JobScope.Content,
                    Name = "Media info extract on import",
                    EventFilter = SingleEventFilter(MagpieEvents.Names.ContentImported),
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = MediaServerRefreshJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Media server refresh on download import",
                    EventFilter = SingleEventFilter(MagpieEvents.Names.DownloadImported),
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = DownloadCleanupJob.Spec.JobType,
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
                    JobType = ContentMetadataRefreshItemJob.Spec.JobType,
                    Scope = JobScope.Content,
                    Name = "Content metadata refresh on creation",
                    EventFilter = SingleEventFilter(MagpieEvents.Names.ContentHierarchyItemCreated),
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = DownloadRefreshJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Download refresh on creation",
                    EventFilter = SingleEventFilter(MagpieEvents.Names.DownloadCreated),
                    DelaySeconds = 15,
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = ReleaseAutoGrabScanJob.Spec.JobType,
                    Scope = JobScope.Content,
                    Name = "Release auto-grab scan on monitoring change",
                    EventFilter = MonitoringChangedEventFilter(),
                    Enabled = true,
                    CreatedAt = now,
                },
                new()
                {
                    JobType = ContentFulfillmentReconciliationJob.Spec.JobType,
                    Scope = JobScope.Administrative,
                    Name = "Content fulfillment reconciliation on quality tier list change",
                    EventFilter = ManyEventFilter(
                        MagpieEvents.Names.QualityTierListCreated,
                        MagpieEvents.Names.QualityTierListUpdated,
                        MagpieEvents.Names.QualityTierListDeleted),
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
                Items = new Dictionary<string, PropertyCriterion<string>>()
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
