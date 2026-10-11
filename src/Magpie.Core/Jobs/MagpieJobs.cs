using System.Collections.Generic;

namespace Magpie.Core.Jobs;

internal static class MagpieJobs
{
    internal static class Settings
    {
        internal const string DiagnosticsGroup = "diagnostics";

        internal const string LastScannedAt = "last-scanned-at";
    }

    internal static class Types
    {
        internal const string ContentFulfillmentReconciliation = "ContentFulfillmentReconciliation";

        internal const string ContentMetadataRefreshItem = "ContentMetadataRefreshItem";

        internal const string ContentMetadataRefreshScan = "ContentMetadataRefreshScan";

        internal const string DatabaseBackup = "DatabaseBackup";

        internal const string DatabaseMaintenance = "DatabaseMaintenance";

        internal const string DiagnosticCheck = "DiagnosticCheck";

        internal const string DownloadCleanup = "DownloadCleanup";

        internal const string DownloadFulfilledCancellation = "DownloadFulfilledCancellation";

        internal const string DownloadImportItem = "DownloadImportItem";

        internal const string DownloadImportScan = "DownloadImportScan";

        internal const string DownloadRefresh = "DownloadRefresh";

        internal const string DownloadStuckDetection = "DownloadStuckDetection";

        internal const string IndexerFeedSyncItem = "IndexerFeedSyncItem";

        internal const string IndexerFeedSyncScan = "IndexerFeedSyncScan";

        internal const string IntegrationHealthCheck = "IntegrationHealthCheck";

        internal const string LibraryImportMatchResolution = "LibraryImportMatchResolution";

        internal const string MediaFileRename = "MediaFileRename";

        internal const string MediaFileReconciliation = "MediaFileReconciliation";

        internal const string MediaInfoExtractItem = "MediaInfoExtractItem";

        internal const string MediaInfoExtractScan = "MediaInfoExtractScan";

        internal const string MediaServerRefresh = "MediaServerRefresh";

        internal const string MetadataContainerCleanup = "MetadataContainerCleanup";

        internal const string ReleaseAutoGrabItem = "ReleaseAutoGrabItem";

        internal const string ReleaseAutoGrabScan = "ReleaseAutoGrabScan";

        internal const string ReleaseDownloadItem = "ReleaseDownloadItem";

        internal const string ReleaseSearchItem = "ReleaseSearchItem";

        internal const string ReleaseSearchScan = "ReleaseSearchScan";

        internal const string RetentionCleanup = "RetentionCleanup";

        internal const string RootFolderContentDiscovery = "RootFolderContentDiscovery";

        internal const string RootFolderContentImportItem = "RootFolderContentImportItem";

        internal const string RootFolderContentImportResume = "RootFolderContentImportResume";

        internal const string SendNotification = "SendNotification";

        internal const string SubjectMaintenance = "SubjectMaintenance";

        internal const string SubjectMetadataRefreshItem = "SubjectMetadataRefreshItem";

        internal const string SubjectMetadataRefreshScan = "SubjectMetadataRefreshScan";

        internal const string WishlistRefreshItem = "WishlistRefreshItem";

        internal const string WishlistRefreshScan = "WishlistRefreshScan";
    }

    internal static class Parameters
    {
        internal const string Associate = "associate";

        internal const string AuditRetentionDays = "auditRetentionDays";

        internal const string BackpressureThreshold = "backpressureThreshold";

        internal const string BackupEnabled = "backupEnabled";

        internal const string BackupIntervalDays = "backupIntervalDays";

        internal const string BackupRetentionCount = "backupRetentionCount";

        internal const string CandidateId = "candidateId";

        internal const string CollisionMode = "collisionMode";

        internal const string RecoveryInactivityMinutes = "recoveryInactivityMinutes";

        internal const string ContentHierarchyItemId = "contentHierarchyItemId";

        internal const string ContentItemId = "contentItemId";

        internal const string DownloadId = "downloadId";

        internal const string DryRun = "dryRun";

        internal const string Event = "event";

        internal const string FailedDownloadRetentionDays = "failedDownloadRetentionDays";

        internal const string FeedId = "feedId";

        internal const string FetchingMinutes = "fetchingMinutes";

        internal const string FilePath = "filePath";

        internal const string Force = "force";

        internal static bool IsForced(IReadOnlyDictionary<string, string>? parameters) =>
            parameters is not null &&
            parameters.TryGetValue(Force, out var raw) &&
            bool.TryParse(raw, out var force) &&
            force;

        internal const string Interactive = "interactive";

        internal const string JobExecutionRetentionDays = "jobExecutionRetentionDays";

        internal const string LeadTimeDays = "leadTimeDays";

        internal const string MatchIndex = "matchIndex";

        internal const string MaxAgeHours = "maxAgeHours";

        internal const string MediaInfoPolicy = "mediaInfoPolicy";

        internal const string MediaFileId = "mediaFileId";

        internal const string MetadataRefreshEndedItemId = "metadataRefreshEndedItemId";

        internal const string MonitoringMode = "monitoringMode";

        internal const string Name = "name";

        internal const string OptimizeEnabled = "optimizeEnabled";

        internal const string OptimizeIntervalDays = "optimizeIntervalDays";

        internal const string ReleaseId = "releaseId";

        internal const string ReleaseObservationRetentionDays = "releaseObservationRetentionDays";

        internal const string RequireMissingMediaInfo = "requireMissingMediaInfo";

        internal const string RetryCooldownHours = "retryCooldownHours";

        internal const string RootFolderId = "rootFolderId";

        internal const string RuleId = "ruleId";

        internal const string SearchKind = "searchKind";

        internal const string SettleMinutes = "settleMinutes";

        internal const string StaleAfterMinutes = "staleAfterMinutes";

        internal const string StalledMinutes = "stalledMinutes";

        internal const string SubjectId = "subjectId";

        internal const string SubjectStalenessDays = "subjectStalenessDays";

        internal const string TimeoutSeconds = "timeoutSeconds";

        internal const string WishlistId = "wishlistId";
    }

    internal static class ParameterValues
    {
        internal const int DefaultRetryCooldownHours = 6;

        internal const string LeafSearchKind = "leaf";

        internal const string MissingMediaInfoPolicy = "Missing";

        internal const string MissingOrStaleMediaInfoPolicy = "MissingOrStale";

        internal const string AllMediaInfoPolicy = "All";

        internal const string NodeSearchKind = "node";
    }

    internal static class StatusMessages
    {
        internal const string UpstreamThrottled = "The work is incomplete while its upstream is throttled.";
    }
}
