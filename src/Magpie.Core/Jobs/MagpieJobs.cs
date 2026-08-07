namespace Magpie.Core.Jobs;

internal static class MagpieJobs
{
    internal static class Types
    {
        internal const string ReleaseDownloadItem = "ReleaseDownloadItem";

        internal const string ReleaseSearchItem = "ReleaseSearchItem";

        internal const string RootFolderContentImportItem = "RootFolderContentImportItem";
    }

    internal static class Parameters
    {
        internal const string CandidateId = "candidateId";

        internal const string ContentHierarchyItemId = "contentHierarchyItemId";

        internal const string ContentItemId = "contentItemId";

        internal const string Event = "event";

        internal const string FeedId = "feedId";

        internal const string FilePath = "filePath";

        internal const string IgnoreFailedTaint = "ignoreFailedTaint";

        internal const string MediaFileId = "mediaFileId";

        internal const string ReleaseId = "releaseId";

        internal const string RequireMissingMediaInfo = "requireMissingMediaInfo";

        internal const string RetryCooldownHours = "retryCooldownHours";

        internal const string RuleId = "ruleId";

        internal const string SearchKind = "searchKind";

        internal const string StaleAfterMinutes = "staleAfterMinutes";

        internal const string SuspectLastResort = "suspectLastResort";

        internal const string TimeoutSeconds = "timeoutSeconds";

        internal const string WishlistId = "wishlistId";
    }

    internal static class ParameterValues
    {
        internal const int DefaultRetryCooldownHours = 6;

        internal const string LeafSearchKind = "leaf";

        internal const string NodeSearchKind = "node";
    }
}
