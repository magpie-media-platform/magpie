namespace Magpie.Sdk.Events;

/// <summary>
/// The stable catalog of Magpie domain event names and event-context keys.
/// </summary>
public static class MagpieEvents
{
    /// <summary>
    /// The stable field names that a changed-field context value lists.
    /// </summary>
    public static class Fields
    {
        /// <summary>
        /// The changed-field name for the enabled state of a user.
        /// </summary>
        public const string UserEnabled = "Enabled";

        /// <summary>
        /// The changed-field name for the role of a user.
        /// </summary>
        public const string UserRole = "Role";
    }

    /// <summary>
    /// The stable event-name strings for the Magpie domain events.
    /// </summary>
    public static class Names
    {
        /// <summary>
        /// The event name for a new API key.
        /// </summary>
        public const string ApiKeyCreated = "apiKey.created";

        /// <summary>
        /// The event name for a deleted API key.
        /// </summary>
        public const string ApiKeyDeleted = "apiKey.deleted";

        /// <summary>
        /// The event name for an updated API key.
        /// </summary>
        public const string ApiKeyUpdated = "apiKey.updated";

        /// <summary>
        /// The event name for a new audit record.
        /// </summary>
        public const string AuditRecordCreated = "audit.record.created";

        /// <summary>
        /// The event name for a completed backup.
        /// </summary>
        public const string BackupCompleted = "backup.completed";

        /// <summary>
        /// The event name for a deleted backup.
        /// </summary>
        public const string BackupDeleted = "backup.deleted";

        /// <summary>
        /// The event name for a failed backup.
        /// </summary>
        public const string BackupFailed = "backup.failed";

        /// <summary>
        /// The event name for a new blocked file type.
        /// </summary>
        public const string BlockedFileTypeCreated = "blockedFileType.created";

        /// <summary>
        /// The event name for a deleted blocked file type.
        /// </summary>
        public const string BlockedFileTypeDeleted = "blockedFileType.deleted";

        /// <summary>
        /// The event name for a new content filter.
        /// </summary>
        public const string ContentFilterCreated = "contentFilter.created";

        /// <summary>
        /// The event name for a deleted content filter.
        /// </summary>
        public const string ContentFilterDeleted = "contentFilter.deleted";

        /// <summary>
        /// The event name for an updated content filter.
        /// </summary>
        public const string ContentFilterUpdated = "contentFilter.updated";

        /// <summary>
        /// The event name for a change to content fulfillment.
        /// </summary>
        public const string ContentFulfillmentChanged = "content.fulfillment.changed";

        /// <summary>
        /// The event name for a new content hierarchy item.
        /// </summary>
        public const string ContentHierarchyItemCreated = "content.hierarchyItem.created";

        /// <summary>
        /// The event name for a deleted content hierarchy item.
        /// </summary>
        public const string ContentHierarchyItemDeleted = "content.hierarchyItem.deleted";

        /// <summary>
        /// The event name for an updated content hierarchy item.
        /// </summary>
        public const string ContentHierarchyItemUpdated = "content.hierarchyItem.updated";

        /// <summary>
        /// The event name for imported content.
        /// </summary>
        public const string ContentImported = "content.imported";

        /// <summary>
        /// The event name for a migrated database.
        /// </summary>
        public const string DatabaseMigrated = "database.migrated";

        /// <summary>
        /// The event name for a raised diagnostic finding.
        /// </summary>
        public const string DiagnosticFindingRaised = "diagnostic.finding.raised";

        /// <summary>
        /// The event name for an updated diagnostic finding.
        /// </summary>
        public const string DiagnosticFindingUpdated = "diagnostic.finding.updated";

        /// <summary>
        /// The event name for a completed diagnostic scan.
        /// </summary>
        public const string DiagnosticScanCompleted = "diagnostic.scan.completed";

        /// <summary>
        /// The event name for a new download client.
        /// </summary>
        public const string DownloadClientCreated = "downloadClient.created";

        /// <summary>
        /// The event name for a deleted download client.
        /// </summary>
        public const string DownloadClientDeleted = "downloadClient.deleted";

        /// <summary>
        /// The event name for an updated download client.
        /// </summary>
        public const string DownloadClientUpdated = "downloadClient.updated";

        /// <summary>
        /// The event name for a completed download.
        /// </summary>
        public const string DownloadCompleted = "download.completed";

        /// <summary>
        /// The event name for a new download.
        /// </summary>
        public const string DownloadCreated = "download.created";

        /// <summary>
        /// The event name for a deleted download.
        /// </summary>
        public const string DownloadDeleted = "download.deleted";

        /// <summary>
        /// The event name for a failed download.
        /// </summary>
        public const string DownloadFailed = "download.failed";

        /// <summary>
        /// The event name for an imported download.
        /// </summary>
        public const string DownloadImported = "download.imported";

        /// <summary>
        /// The event name for a stalled download.
        /// </summary>
        public const string DownloadStalled = "download.stalled";

        /// <summary>
        /// The event name for an updated download.
        /// </summary>
        public const string DownloadUpdated = "download.updated";

        /// <summary>
        /// The event name for a new event trigger.
        /// </summary>
        public const string EventTriggerCreated = "eventTrigger.created";

        /// <summary>
        /// The event name for a deleted event trigger.
        /// </summary>
        public const string EventTriggerDeleted = "eventTrigger.deleted";

        /// <summary>
        /// The event name for an updated event trigger.
        /// </summary>
        public const string EventTriggerUpdated = "eventTrigger.updated";

        /// <summary>
        /// The event name for a change of the activation state of the extensions.
        /// </summary>
        public const string ExtensionActivationChanged = "extension.activation.changed";

        /// <summary>
        /// The event name for a disabled extension.
        /// </summary>
        public const string ExtensionDisabled = "extension.disabled";

        /// <summary>
        /// The event name for an enabled extension.
        /// </summary>
        public const string ExtensionEnabled = "extension.enabled";

        /// <summary>
        /// The event name for an installed extension.
        /// </summary>
        public const string ExtensionInstalled = "extension.installed";

        /// <summary>
        /// The event name for a new extension package source.
        /// </summary>
        public const string ExtensionPackageSourceCreated = "extensionPackageSource.created";

        /// <summary>
        /// The event name for a deleted extension package source.
        /// </summary>
        public const string ExtensionPackageSourceDeleted = "extensionPackageSource.deleted";

        /// <summary>
        /// The event name for an updated extension package source.
        /// </summary>
        public const string ExtensionPackageSourceUpdated = "extensionPackageSource.updated";

        /// <summary>
        /// The event name for an uninstalled extension.
        /// </summary>
        public const string ExtensionUninstalled = "extension.uninstalled";

        /// <summary>
        /// The event name for an updated extension.
        /// </summary>
        public const string ExtensionUpdated = "extension.updated";

        /// <summary>
        /// The event name for an upgraded extension.
        /// </summary>
        public const string ExtensionUpgraded = "extension.upgraded";

        /// <summary>
        /// The event name for a new indexer.
        /// </summary>
        public const string IndexerCreated = "indexer.created";

        /// <summary>
        /// The event name for a deleted indexer.
        /// </summary>
        public const string IndexerDeleted = "indexer.deleted";

        /// <summary>
        /// The event name for an updated indexer.
        /// </summary>
        public const string IndexerUpdated = "indexer.updated";

        /// <summary>
        /// The event name for a new indexer feed.
        /// </summary>
        public const string IndexerFeedCreated = "indexerFeed.created";

        /// <summary>
        /// The event name for a deleted indexer feed.
        /// </summary>
        public const string IndexerFeedDeleted = "indexerFeed.deleted";

        /// <summary>
        /// The event name for an updated indexer feed.
        /// </summary>
        public const string IndexerFeedUpdated = "indexerFeed.updated";

        /// <summary>
        /// The event name for a change to integration health.
        /// </summary>
        public const string IntegrationHealthChanged = "integration.health.changed";

        /// <summary>
        /// The event name for a failed job execution.
        /// </summary>
        public const string JobExecutionFailed = "job.execution.failed";

        /// <summary>
        /// The event name for a request to cancel a job execution.
        /// </summary>
        public const string JobExecutionCancellationRequested = "job.execution.cancellationRequested";

        /// <summary>
        /// The event name for a cancelled job execution.
        /// </summary>
        public const string JobExecutionCancelled = "job.execution.cancelled";

        /// <summary>
        /// The event name for a completed job execution.
        /// </summary>
        public const string JobExecutionCompleted = "job.execution.completed";

        /// <summary>
        /// The event name for a new job execution.
        /// </summary>
        public const string JobExecutionCreated = "job.execution.created";

        /// <summary>
        /// The event name for a change to job execution progress.
        /// </summary>
        public const string JobExecutionProgressChanged = "job.execution.progress.changed";

        /// <summary>
        /// The event name for a queued job execution.
        /// </summary>
        public const string JobExecutionQueued = "job.execution.queued";

        /// <summary>
        /// The event name for a started job execution.
        /// </summary>
        public const string JobExecutionStarted = "job.execution.started";

        /// <summary>
        /// The event name for an updated library import candidate.
        /// </summary>
        public const string LibraryImportCandidateUpdated = "library.importCandidate.updated";

        /// <summary>
        /// The event name for a completed library scan.
        /// </summary>
        public const string LibraryScanCompleted = "library.scan.completed";

        /// <summary>
        /// The event name for a new media server.
        /// </summary>
        public const string MediaServerCreated = "mediaServer.created";

        /// <summary>
        /// The event name for a deleted media server.
        /// </summary>
        public const string MediaServerDeleted = "mediaServer.deleted";

        /// <summary>
        /// The event name for an updated media server.
        /// </summary>
        public const string MediaServerUpdated = "mediaServer.updated";

        /// <summary>
        /// The event name for refreshed metadata.
        /// </summary>
        public const string MetadataRefreshed = "metadata.refreshed";

        /// <summary>
        /// The event name for a new MIME mapping.
        /// </summary>
        public const string MimeMappingCreated = "mimeMapping.created";

        /// <summary>
        /// The event name for a deleted MIME mapping.
        /// </summary>
        public const string MimeMappingDeleted = "mimeMapping.deleted";

        /// <summary>
        /// The event name for an updated MIME mapping.
        /// </summary>
        public const string MimeMappingUpdated = "mimeMapping.updated";

        /// <summary>
        /// The event name for a new notification rule.
        /// </summary>
        public const string NotificationRuleCreated = "notificationRule.created";

        /// <summary>
        /// The event name for a deleted notification rule.
        /// </summary>
        public const string NotificationRuleDeleted = "notificationRule.deleted";

        /// <summary>
        /// The event name for an updated notification rule.
        /// </summary>
        public const string NotificationRuleUpdated = "notificationRule.updated";

        /// <summary>
        /// The event name for a test notification.
        /// </summary>
        public const string NotificationTest = "magpie.notification.test";

        /// <summary>
        /// The event name for a new quality tier list.
        /// </summary>
        public const string QualityTierListCreated = "qualityTierList.created";

        /// <summary>
        /// The event name for a deleted quality tier list.
        /// </summary>
        public const string QualityTierListDeleted = "qualityTierList.deleted";

        /// <summary>
        /// The event name for an updated quality tier list.
        /// </summary>
        public const string QualityTierListUpdated = "qualityTierList.updated";

        /// <summary>
        /// The event name for an updated quality tier.
        /// </summary>
        public const string QualityTierUpdated = "qualityTier.updated";

        /// <summary>
        /// The event name for a rejected release grab.
        /// </summary>
        public const string ReleaseGrabRejected = "releaseGrab.rejected";

        /// <summary>
        /// The event name for a release that matches the item that a search started from.
        /// </summary>
        public const string ReleaseMatched = "release.matched";

        /// <summary>
        /// The event name for a release that matches items other than the item that a search started from.
        /// </summary>
        public const string ReleaseScopeMatched = "release.scopeMatched";

        /// <summary>
        /// The event name for a completed release search.
        /// </summary>
        public const string ReleaseSearchCompleted = "releaseSearch.completed";

        /// <summary>
        /// The event name for a new remote path mapping.
        /// </summary>
        public const string RemotePathMappingCreated = "remotePathMapping.created";

        /// <summary>
        /// The event name for a deleted remote path mapping.
        /// </summary>
        public const string RemotePathMappingDeleted = "remotePathMapping.deleted";

        /// <summary>
        /// The event name for an updated remote path mapping.
        /// </summary>
        public const string RemotePathMappingUpdated = "remotePathMapping.updated";

        /// <summary>
        /// The event name for a new root folder.
        /// </summary>
        public const string RootFolderCreated = "library.rootFolder.created";

        /// <summary>
        /// The event name for a deleted root folder.
        /// </summary>
        public const string RootFolderDeleted = "library.rootFolder.deleted";

        /// <summary>
        /// The event name for an updated root folder.
        /// </summary>
        public const string RootFolderUpdated = "library.rootFolder.updated";

        /// <summary>
        /// The event name for a new schedule trigger.
        /// </summary>
        public const string ScheduleTriggerCreated = "scheduleTrigger.created";

        /// <summary>
        /// The event name for a deleted schedule trigger.
        /// </summary>
        public const string ScheduleTriggerDeleted = "scheduleTrigger.deleted";

        /// <summary>
        /// The event name for an updated schedule trigger.
        /// </summary>
        public const string ScheduleTriggerUpdated = "scheduleTrigger.updated";

        /// <summary>
        /// The event name for an updated settings group.
        /// </summary>
        public const string SettingsGroupUpdated = "settingsGroup.updated";

        /// <summary>
        /// The event name for a new subject.
        /// </summary>
        public const string SubjectCreated = "subject.created";

        /// <summary>
        /// The event name for a deleted subject.
        /// </summary>
        public const string SubjectDeleted = "subject.deleted";

        /// <summary>
        /// The event name for an updated subject.
        /// </summary>
        public const string SubjectUpdated = "subject.updated";

        /// <summary>
        /// The event name for a new subject filter.
        /// </summary>
        public const string SubjectFilterCreated = "subjectFilter.created";

        /// <summary>
        /// The event name for a deleted subject filter.
        /// </summary>
        public const string SubjectFilterDeleted = "subjectFilter.deleted";

        /// <summary>
        /// The event name for an updated subject filter.
        /// </summary>
        public const string SubjectFilterUpdated = "subjectFilter.updated";

        /// <summary>
        /// The event name for a new user.
        /// </summary>
        public const string UserCreated = "user.created";

        /// <summary>
        /// The event name for a deleted user.
        /// </summary>
        public const string UserDeleted = "user.deleted";

        /// <summary>
        /// The event name for a locked-out user.
        /// </summary>
        public const string UserLockedOut = "user.lockedOut";

        /// <summary>
        /// The event name for an unlocked user.
        /// </summary>
        public const string UserUnlocked = "user.unlocked";

        /// <summary>
        /// The event name for an updated user.
        /// </summary>
        public const string UserUpdated = "user.updated";

        /// <summary>
        /// The event name for a new wishlist.
        /// </summary>
        public const string WishlistCreated = "wishlist.created";

        /// <summary>
        /// The event name for a deleted wishlist.
        /// </summary>
        public const string WishlistDeleted = "wishlist.deleted";

        /// <summary>
        /// The event name for a matched wishlist item.
        /// </summary>
        public const string WishlistItemMatched = "wishlist.item.matched";

        /// <summary>
        /// The event name for an updated wishlist.
        /// </summary>
        public const string WishlistUpdated = "wishlist.updated";
    }

    /// <summary>
    /// The stable context keys that Magpie events carry.
    /// </summary>
    public static class Keys
    {
        /// <summary>
        /// The context key for the id of the API key.
        /// </summary>
        public const string ApiKeyId = "apiKey.id";

        /// <summary>
        /// The context key for the changed fields of an API key.
        /// </summary>
        public const string ApiKeyChangedFields = "apiKey.changedFields";

        /// <summary>
        /// The context key for the id of the audit record.
        /// </summary>
        public const string AuditRecordId = "audit.recordId";

        /// <summary>
        /// The context key for the id of the backup.
        /// </summary>
        public const string BackupId = "backup.id";

        /// <summary>
        /// The context key for the file path of the backup.
        /// </summary>
        public const string BackupPath = "backup.path";

        /// <summary>
        /// The context key for the size of the backup in bytes.
        /// </summary>
        public const string BackupSizeBytes = "backup.sizeBytes";

        /// <summary>
        /// The context key for the reason for a failed backup.
        /// </summary>
        public const string BackupFailureReason = "backup.failureReason";

        /// <summary>
        /// The context key for the id of the blocked file type.
        /// </summary>
        public const string BlockedFileTypeId = "blockedFileType.id";

        /// <summary>
        /// The context key for the id of the content item.
        /// </summary>
        public const string ContentItemId = "content.itemId";

        /// <summary>
        /// The context key for the id of the content hierarchy item.
        /// </summary>
        public const string ContentHierarchyItemId = "content.hierarchyItemId";

        /// <summary>
        /// The context key for the changed fields of a content hierarchy item.
        /// </summary>
        public const string ContentChangedFields = "content.changedFields";

        /// <summary>
        /// The context key for the fulfillment status before a fulfillment change.
        /// </summary>
        public const string ContentFulfillmentFrom = "content.fulfillment.from";

        /// <summary>
        /// The context key for the fulfillment status after a fulfillment change.
        /// </summary>
        public const string ContentFulfillmentTo = "content.fulfillment.to";

        /// <summary>
        /// The context key for the number of imported files.
        /// </summary>
        public const string ContentImportedFileCount = "content.importedFileCount";

        /// <summary>
        /// The context key for the id of the metadata container of the changed item.
        /// </summary>
        public const string ContentImpactMetadataContainerId = "content.impact.metadataContainerId";

        /// <summary>
        /// The context key for the id of the parent of the changed item.
        /// </summary>
        public const string ContentImpactParentId = "content.impact.parentId";

        /// <summary>
        /// The context key for the ids of the ancestors of the changed item.
        /// </summary>
        public const string ContentImpactAncestorIds = "content.impact.ancestorIds";

        /// <summary>
        /// The context key for the id of the top-level ancestor of the changed item.
        /// </summary>
        public const string ContentImpactRootId = "content.impact.rootId";

        /// <summary>
        /// The context key for the content type slug of the changed item.
        /// </summary>
        public const string ContentImpactContentType = "content.impact.contentType";

        /// <summary>
        /// The context key for the hierarchy level slug of the changed item.
        /// </summary>
        public const string ContentImpactLevel = "content.impact.level";

        /// <summary>
        /// The context key for the top-level flag of the changed item.
        /// </summary>
        public const string ContentImpactTopLevel = "content.impact.topLevel";

        /// <summary>
        /// The context key for the id of the root folder of the changed item.
        /// </summary>
        public const string ContentImpactRootFolderId = "content.impact.rootFolderId";

        /// <summary>
        /// The context key for the id of the quality tier list of the changed item.
        /// </summary>
        public const string ContentImpactQualityTierListId = "content.impact.qualityTierListId";

        /// <summary>
        /// The context key for the monitoring flag of the changed item.
        /// </summary>
        public const string ContentImpactMonitoring = "content.impact.monitoring";

        /// <summary>
        /// The context key for the fulfillment status of the changed content item.
        /// </summary>
        public const string ContentImpactFulfillment = "content.impact.fulfillment";

        /// <summary>
        /// The context key for the id of the wishlist that requested the changed item.
        /// </summary>
        public const string ContentImpactWishlistId = "content.impact.wishlistId";

        /// <summary>
        /// The context key for the ids of the items removed with the changed item.
        /// </summary>
        public const string ContentImpactSubtreeIds = "content.impact.subtreeIds";

        /// <summary>
        /// The context key for the flag that marks a truncated subtree id list.
        /// </summary>
        public const string ContentImpactSubtreeTruncated = "content.impact.subtreeTruncated";

        /// <summary>
        /// The context key for the ids of the items that a batch operation affects.
        /// </summary>
        public const string ContentImpactBatchIds = "content.impact.batchIds";

        /// <summary>
        /// The context key for the flag that marks a truncated batch id list.
        /// </summary>
        public const string ContentImpactBatchTruncated = "content.impact.batchTruncated";

        /// <summary>
        /// The context key for the id of the source metadata container.
        /// </summary>
        public const string ContentImpactMetadataSourceContainerId = "content.impact.metadata.sourceContainerId";

        /// <summary>
        /// The context key for the ids of the metadata containers that gained a relationship.
        /// </summary>
        public const string ContentImpactMetadataAddedContainerIds = "content.impact.metadata.addedContainerIds";

        /// <summary>
        /// The context key for the ids of the metadata containers that lost a relationship.
        /// </summary>
        public const string ContentImpactMetadataRemovedContainerIds = "content.impact.metadata.removedContainerIds";

        /// <summary>
        /// The context key for the id of the parent before the change.
        /// </summary>
        public const string ContentImpactPreviousParentId = "content.impact.previous.parentId";

        /// <summary>
        /// The context key for the ids of the ancestors before the change.
        /// </summary>
        public const string ContentImpactPreviousAncestorIds = "content.impact.previous.ancestorIds";

        /// <summary>
        /// The context key for the id of the top-level ancestor before the change.
        /// </summary>
        public const string ContentImpactPreviousRootId = "content.impact.previous.rootId";

        /// <summary>
        /// The context key for the content type slug before the change.
        /// </summary>
        public const string ContentImpactPreviousContentType = "content.impact.previous.contentType";

        /// <summary>
        /// The context key for the hierarchy level slug before the change.
        /// </summary>
        public const string ContentImpactPreviousLevel = "content.impact.previous.level";

        /// <summary>
        /// The context key for the top-level flag before the change.
        /// </summary>
        public const string ContentImpactPreviousTopLevel = "content.impact.previous.topLevel";

        /// <summary>
        /// The context key for the id of the root folder before the change.
        /// </summary>
        public const string ContentImpactPreviousRootFolderId = "content.impact.previous.rootFolderId";

        /// <summary>
        /// The context key for the id of the quality tier list before the change.
        /// </summary>
        public const string ContentImpactPreviousQualityTierListId = "content.impact.previous.qualityTierListId";

        /// <summary>
        /// The context key for the monitoring flag before the change.
        /// </summary>
        public const string ContentImpactPreviousMonitoring = "content.impact.previous.monitoring";

        /// <summary>
        /// The context key for the fulfillment status before the change.
        /// </summary>
        public const string ContentImpactPreviousFulfillment = "content.impact.previous.fulfillment";

        /// <summary>
        /// The context key for the id of the content filter.
        /// </summary>
        public const string ContentFilterId = "contentFilter.id";

        /// <summary>
        /// The context key for the changed fields of a content filter.
        /// </summary>
        public const string ContentFilterChangedFields = "contentFilter.changedFields";

        /// <summary>
        /// The context key for the id of the diagnostic finding.
        /// </summary>
        public const string DiagnosticFindingId = "diagnostic.findingId";

        /// <summary>
        /// The context key for the category of the diagnostic finding.
        /// </summary>
        public const string DiagnosticCategory = "diagnostic.category";

        /// <summary>
        /// The context key for the severity of the diagnostic finding.
        /// </summary>
        public const string DiagnosticSeverity = "diagnostic.severity";

        /// <summary>
        /// The context key for the number of findings in a diagnostic scan.
        /// </summary>
        public const string DiagnosticFindingCount = "diagnostic.findingCount";

        /// <summary>
        /// The context key for the id of the download.
        /// </summary>
        public const string DownloadId = "download.id";

        /// <summary>
        /// The context key for the id of the downloaded release.
        /// </summary>
        public const string DownloadReleaseId = "download.releaseId";

        /// <summary>
        /// The context key for the reason for a failed download.
        /// </summary>
        public const string DownloadFailureReason = "download.failureReason";

        /// <summary>
        /// The context key for the id of the indexer that supplied the download.
        /// </summary>
        public const string DownloadIndexerId = "download.indexerId";

        /// <summary>
        /// The context key for the reason for a stalled download.
        /// </summary>
        public const string DownloadStallReason = "download.stallReason";

        /// <summary>
        /// The context key for the changed fields of a download.
        /// </summary>
        public const string DownloadChangedFields = "download.changedFields";

        /// <summary>
        /// The context key for the id of the download client.
        /// </summary>
        public const string DownloadClientId = "downloadClient.id";

        /// <summary>
        /// The context key for the changed fields of a download client.
        /// </summary>
        public const string DownloadClientChangedFields = "downloadClient.changedFields";

        /// <summary>
        /// The context key for the id of the event trigger.
        /// </summary>
        public const string EventTriggerId = "eventTrigger.id";

        /// <summary>
        /// The context key for the changed fields of an event trigger.
        /// </summary>
        public const string EventTriggerChangedFields = "eventTrigger.changedFields";

        /// <summary>
        /// The context key for the id of the job execution.
        /// </summary>
        public const string ExecutionId = "execution.id";

        /// <summary>
        /// The context key for the job type of the execution.
        /// </summary>
        public const string ExecutionJobType = "execution.jobType";

        /// <summary>
        /// The context key for the reason for a failed job execution.
        /// </summary>
        public const string ExecutionFailureReason = "execution.failureReason";

        /// <summary>
        /// The context key for the id of the extension.
        /// </summary>
        public const string ExtensionId = "extension.id";

        /// <summary>
        /// The context key for the assembly name of the extension.
        /// </summary>
        public const string ExtensionAssemblyName = "extension.assemblyName";

        /// <summary>
        /// The context key for the file path of the extension.
        /// </summary>
        public const string ExtensionFilePath = "extension.filePath";

        /// <summary>
        /// The context key for the generation of the activation state of the extensions.
        /// </summary>
        public const string ExtensionGeneration = "extension.generation";

        /// <summary>
        /// The context key for the reason for an extension state change.
        /// </summary>
        public const string ExtensionReason = "extension.reason";

        /// <summary>
        /// The context key for the version of the extension.
        /// </summary>
        public const string ExtensionVersion = "extension.version";

        /// <summary>
        /// The context key for the extension version before an upgrade.
        /// </summary>
        public const string ExtensionFromVersion = "extension.fromVersion";

        /// <summary>
        /// The context key for the extension version after an upgrade.
        /// </summary>
        public const string ExtensionToVersion = "extension.toVersion";

        /// <summary>
        /// The context key for the changed fields of an extension.
        /// </summary>
        public const string ExtensionChangedFields = "extension.changedFields";

        /// <summary>
        /// The context key for the id of the extension package source.
        /// </summary>
        public const string ExtensionPackageSourceId = "extensionPackageSource.id";

        /// <summary>
        /// The context key for the changed fields of an extension package source.
        /// </summary>
        public const string ExtensionPackageSourceChangedFields = "extensionPackageSource.changedFields";

        /// <summary>
        /// The context key for the id of the indexer.
        /// </summary>
        public const string IndexerId = "indexer.id";

        /// <summary>
        /// The context key for the changed fields of an indexer.
        /// </summary>
        public const string IndexerChangedFields = "indexer.changedFields";

        /// <summary>
        /// The context key for the id of the indexer feed.
        /// </summary>
        public const string IndexerFeedId = "indexerFeed.id";

        /// <summary>
        /// The context key for the changed fields of an indexer feed.
        /// </summary>
        public const string IndexerFeedChangedFields = "indexerFeed.changedFields";

        /// <summary>
        /// The context key for the id of the integration.
        /// </summary>
        public const string IntegrationId = "integration.id";

        /// <summary>
        /// The context key for the type of the integration.
        /// </summary>
        public const string IntegrationType = "integration.type";

        /// <summary>
        /// The context key for the integration health before the change.
        /// </summary>
        public const string IntegrationFrom = "integration.from";

        /// <summary>
        /// The context key for the integration health after the change.
        /// </summary>
        public const string IntegrationTo = "integration.to";

        /// <summary>
        /// The context key for the id of the library import candidate.
        /// </summary>
        public const string LibraryImportCandidateId = "library.importCandidateId";

        /// <summary>
        /// The context key for the id of the media server.
        /// </summary>
        public const string MediaServerId = "mediaServer.id";

        /// <summary>
        /// The context key for the changed fields of a media server.
        /// </summary>
        public const string MediaServerChangedFields = "mediaServer.changedFields";

        /// <summary>
        /// The context key for the file extension of the MIME mapping.
        /// </summary>
        public const string MimeMappingExtension = "mimeMapping.extension";

        /// <summary>
        /// The context key for the changed fields of a MIME mapping.
        /// </summary>
        public const string MimeMappingChangedFields = "mimeMapping.changedFields";

        /// <summary>
        /// The context key for the id of the notification rule.
        /// </summary>
        public const string NotificationRuleId = "notificationRule.id";

        /// <summary>
        /// The context key for the changed fields of a notification rule.
        /// </summary>
        public const string NotificationRuleChangedFields = "notificationRule.changedFields";

        /// <summary>
        /// The context key for the id of the rule that a notification event reports.
        /// </summary>
        public const string RuleId = "rule.id";

        /// <summary>
        /// The context key for the name of the rule that a notification event reports.
        /// </summary>
        public const string RuleName = "rule.name";

        /// <summary>
        /// The context key for the flag that marks a test notification.
        /// </summary>
        public const string Test = "test";

        /// <summary>
        /// The context key for the id of the quality tier list.
        /// </summary>
        public const string QualityTierListId = "qualityTierList.id";

        /// <summary>
        /// The context key for the changed fields of a quality tier list.
        /// </summary>
        public const string QualityTierListChangedFields = "qualityTierList.changedFields";

        /// <summary>
        /// The context key for the id of the quality tier.
        /// </summary>
        public const string QualityTierId = "qualityTier.id";

        /// <summary>
        /// The context key for the changed fields of a quality tier.
        /// </summary>
        public const string QualityTierChangedFields = "qualityTier.changedFields";

        /// <summary>
        /// The context key for the id of the release.
        /// </summary>
        public const string ReleaseId = "release.id";

        /// <summary>
        /// The context key for the reason for a rejected release grab.
        /// </summary>
        public const string ReleaseRejectionReason = "release.rejectionReason";

        /// <summary>
        /// The context key for the number of releases that a search found.
        /// </summary>
        public const string ReleaseSearchCount = "releaseSearch.count";

        /// <summary>
        /// The context key for the id of the remote path mapping.
        /// </summary>
        public const string RemotePathMappingId = "remotePathMapping.id";

        /// <summary>
        /// The context key for the changed fields of a remote path mapping.
        /// </summary>
        public const string RemotePathMappingChangedFields = "remotePathMapping.changedFields";

        /// <summary>
        /// The context key for the id of the root folder.
        /// </summary>
        public const string RootFolderId = "rootFolder.id";

        /// <summary>
        /// The context key for the file path of the root folder.
        /// </summary>
        public const string RootFolderPath = "rootFolder.path";

        /// <summary>
        /// The context key for the content type of the root folder.
        /// </summary>
        public const string RootFolderContentType = "rootFolder.contentType";

        /// <summary>
        /// The context key for the changed fields of a root folder.
        /// </summary>
        public const string RootFolderChangedFields = "rootFolder.changedFields";

        /// <summary>
        /// The context key for the summary of a library scan.
        /// </summary>
        public const string ScanSummary = "scan.summary";

        /// <summary>
        /// The context key for the id of the schedule trigger.
        /// </summary>
        public const string ScheduleTriggerId = "scheduleTrigger.id";

        /// <summary>
        /// The context key for the changed fields of a schedule trigger.
        /// </summary>
        public const string ScheduleTriggerChangedFields = "scheduleTrigger.changedFields";

        /// <summary>
        /// The context key for the name of the settings group.
        /// </summary>
        public const string SettingsGroupName = "settingsGroup.name";

        /// <summary>
        /// The context key for the id of the extension that owns the settings group.
        /// </summary>
        public const string SettingsGroupExtensionId = "settingsGroup.extensionId";

        /// <summary>
        /// The context key for the changed fields of a settings group.
        /// </summary>
        public const string SettingsGroupChangedFields = "settingsGroup.changedFields";

        /// <summary>
        /// The context key for the id of the subject.
        /// </summary>
        public const string SubjectId = "subject.id";

        /// <summary>
        /// The context key for the changed fields of a subject.
        /// </summary>
        public const string SubjectChangedFields = "subject.changedFields";

        /// <summary>
        /// The context key for the id of the subject filter.
        /// </summary>
        public const string SubjectFilterId = "subjectFilter.id";

        /// <summary>
        /// The context key for the changed fields of a subject filter.
        /// </summary>
        public const string SubjectFilterChangedFields = "subjectFilter.changedFields";

        /// <summary>
        /// The context key for the id of the user.
        /// </summary>
        public const string UserId = "user.id";

        /// <summary>
        /// The context key for the username of the user.
        /// </summary>
        public const string UserUsername = "user.username";

        /// <summary>
        /// The context key for the role of the user.
        /// </summary>
        public const string UserRole = "user.role";

        /// <summary>
        /// The context key for the changed fields of a user.
        /// </summary>
        public const string UserChangedFields = "user.changedFields";

        /// <summary>
        /// The context key for the number of failed sign-in attempts.
        /// </summary>
        public const string UserFailedAttemptCount = "user.failedAttemptCount";

        /// <summary>
        /// The context key for the time when the user lockout ends.
        /// </summary>
        public const string UserLockedUntil = "user.lockedUntil";

        /// <summary>
        /// The context key for the reason for a user unlock.
        /// </summary>
        public const string UserUnlockReason = "user.unlockReason";

        /// <summary>
        /// The context key for the id of the wishlist.
        /// </summary>
        public const string WishlistId = "wishlist.id";

        /// <summary>
        /// The context key for the id of the wishlist item.
        /// </summary>
        public const string WishlistItemId = "wishlist.itemId";

        /// <summary>
        /// The context key for the changed fields of a wishlist.
        /// </summary>
        public const string WishlistChangedFields = "wishlist.changedFields";
    }
}
