using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Core.Observability;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Releases;
using Magpie.Sdk.Events;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class DownloadImportItemJob(
    IAuditRecordRepository auditRecordRepository,
    IDownloadRepository downloadRepository,
    IRootFolderRepository rootFolderRepository,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IMediaFileRepository mediaFileRepository,
    IReleaseRepository releaseRepository,
    IReleaseScopeRepository releaseScopeRepository,
    IDownloadScopeRepository downloadScopeRepository,
    IJobExecutionRepository jobExecutionRepository,
    IFileSystem fileSystem,
    IContentTypeRegistry contentTypeRegistry,
    IContentFileService contentFileService,
    IBlockedFileTypeRepository blockedFileTypeRepository,
    IRemotePathMappingRepository remotePathMappingRepository,
    IDownloadClientRepository downloadClientRepository,
    IRemotePathMappingService remotePathMappingService,
    ITransactionManager transactionManager,
    IQualityService qualityService,
    IContentRepository contentRepository,
    IContentService contentService,
    ILibraryImportService libraryImportService,
    IEventBus eventBus,
    IReleaseEvaluationService releaseEvaluationService,
    TimeProvider timeProvider,
    ILogger<DownloadImportItemJob> logger,
    IProgressReporter progressReporter) : IJob
{
    private enum DownloadIdSource
    {
        Parameter,
        EventContext,
    }

    private readonly record struct ImportFileOperation(
        string Source,
        string Destination,
        FileKind FileKind,
        bool IsMainContent);

    private readonly record struct MediaFileProbe(string FilePath, FileKind FileKind, string? MimeType, long FileSize);

    private readonly record struct TransferredOperation(ImportFileOperation Operation, ImportMode ExecutedMode);

    private readonly record struct LeafImportGroup(
        ContentItem ContentItem,
        IReadOnlyList<TransferredOperation> Files,
        bool UnresolvedContainerBinding);

    private readonly record struct ReplaceSnapshot(MediaFile? ExistingMediaFile, long PriorSize);

    private readonly record struct ReplaceBackup(string OriginalPath, string BackupPath);

    private readonly record struct TransferResult(
        IReadOnlyList<TransferredOperation> Transferred,
        IReadOnlyList<ReplaceBackup> ReplaceBackups);

    private readonly record struct ResolvedLeafImport(
        ContentItem ContentItem,
        IReadOnlyList<TransferredOperation> Files,
        bool UnresolvedContainerBinding,
        FulfillmentStatus Prior,
        QualityStatus? QualityStatus);

    private sealed record ImportOperation
    {
        public required IReadOnlyList<ImportFileOperation> PlannedOperations { get; init; }

        public required IReadOnlyList<ImportFileOperation> FilteredOperations { get; init; }

        public required IReadOnlyList<string> ReplaceTargets { get; init; }
    }

    private sealed class DownloadFileSafetyException(string message) : InvalidOperationException(message);

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static readonly HashSet<string> CommonIgnoredSidecarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".nfo"
    };

    private static readonly IReadOnlyList<string> TwoLetterLanguageCodes =
    [
        "en", "es", "fr", "de", "it", "pt", "ru", "ja", "zh", "ko",
        "ar", "nl", "pl", "sv", "da", "no", "fi", "cs", "hu", "tr",
        "el", "he", "th", "vi", "id", "ms", "ro", "uk", "bg", "sk",
        "hr", "sl", "et", "lv", "lt", "ca", "hi", "bn", "ta", "te",
        "ml", "kn", "mr", "pa", "ur", "fa",
    ];

    private static readonly IReadOnlyList<string> ThreeLetterLanguageCodes =
    [
        "eng", "spa", "fre", "fra", "ger", "deu", "ita", "por", "rus", "jpn",
        "chi", "zho", "kor", "ara", "dut", "nld", "pol", "swe", "dan", "nor",
        "fin", "cze", "ces", "hun", "tur", "gre", "ell", "heb", "tha", "vie",
        "ind", "may", "msa", "rum", "ron", "ukr", "bul", "slo", "slk", "hrv",
        "slv", "est", "lav", "lit", "cat", "hin", "ben", "tam", "tel", "mal",
        "kan", "mar", "pan", "urd", "per", "fas",
    ];

    private static readonly Regex LanguageSuffixPattern = new(
        @"^(?<base>.*)\.(?<lang>[A-Za-z]{2}(-[A-Z]{2})?|[A-Za-z]{3})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.DownloadImportItem,
        DisplayName = "Download import item",
        Description = "Imports a completed download into its content library.",
        Category = "Integrations",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        Timeout = TimeSpan.FromMinutes(30),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTypeAndTarget,
        LockPartitionFields = [MagpieJobs.Parameters.DownloadId],
        Parameters =
        [
            new(
                MagpieJobs.Parameters.DownloadId,
                "The download id to import.",
                DataType.String,
                true,
                null),
            new(
                MagpieJobs.Parameters.DryRun,
                "When true, plan the import without moving files or writing the DB.",
                DataType.Boolean,
                false,
                "false"),
            new(
                MagpieJobs.Parameters.CollisionMode,
                "How to treat destination collisions: 'fail', 'skip', or 'replace'. " +
                "Defaults to the root folder's configured DefaultImportCollisionMode.",
                DataType.String,
                false,
                "fail")
        ],
    };

    private const string TorrentProtocol = "torrent";

    private static IReadOnlyList<ContentHierarchyLevelDefinition> GetLevelsAtOrUnder(
        ContentTypeDefinition? definition,
        string levelSlug)
    {
        var levels = definition?.LevelDefinitions ?? [];
        if (levels.Count == 0 || string.IsNullOrWhiteSpace(levelSlug))
        {
            return [];
        }

        var branch = new HashSet<string>(StringComparer.Ordinal) { levelSlug };
        bool grew;
        do
        {
            grew = false;
            foreach (var level in levels)
            {
                if (level.ParentSlug is { } parent && branch.Contains(parent) && branch.Add(level.Slug))
                {
                    grew = true;
                }
            }
        }
        while (grew);

        return [.. levels.Where(level => branch.Contains(level.Slug))];
    }

    private static bool TryResolveDownloadId(JobContext context, out string rawDownloadId, out DownloadIdSource source)
    {
        if (context.Parameters.TryGetValue(MagpieJobs.Parameters.DownloadId, out var rawParameter) &&
            !string.IsNullOrWhiteSpace(rawParameter))
        {
            rawDownloadId = rawParameter;
            source = DownloadIdSource.Parameter;
            return true;
        }

        if (context.Event is { Context: var eventContext } &&
            eventContext.TryGetValue(MagpieEvents.Keys.DownloadId, out var rawFromEvent) &&
            !string.IsNullOrWhiteSpace(rawFromEvent))
        {
            rawDownloadId = rawFromEvent;
            source = DownloadIdSource.EventContext;
            return true;
        }

        rawDownloadId = string.Empty;
        source = DownloadIdSource.Parameter;
        return false;
    }

    private static bool IsCommonIgnoredSidecar(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(extension) && CommonIgnoredSidecarExtensions.Contains(extension);
    }

    private static bool HasSampleDirectorySegment(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        return !string.IsNullOrEmpty(directory) && directory
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Contains("sample", StringComparer.OrdinalIgnoreCase);
    }

    private static ImportMode ResolveTransferMode(string? protocol)
    {
        return string.Equals(protocol, TorrentProtocol, StringComparison.OrdinalIgnoreCase)
            ? ImportMode.Copy
            : ImportMode.Move;
    }

    private static void RecordTransferOutcome(Activity? activity, int fileCount, string outcome)
    {
        activity?.SetTag("magpie.import.outcome", outcome);
        var outcomeTag = new KeyValuePair<string, object?>("outcome", outcome);
        DownloadTelemetry.ImportFiles.Record(fileCount, outcomeTag);
    }

    private static void SetFailureReason(Activity? activity, Activity? importActivity, DownloadFailureReason reason)
    {
        var name = reason.ToString();
        activity?.SetTag("magpie.download.failure_reason", name);
        activity?.SetStatus(ActivityStatusCode.Error, name);
        importActivity?.SetTag("magpie.download.failure_reason", name);
        importActivity?.SetStatus(ActivityStatusCode.Error, name);
    }

    private static string TransferModeName(ImportMode mode)
    {
        return mode == ImportMode.Copy ? "copy" : "move";
    }

    private static string NormaliseBlockedExtension(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return string.Empty;
        }

        var trimmed = pattern.Trim();
        if (trimmed.StartsWith("*.", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }
        else if (!trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            trimmed = "." + trimmed;
        }

        return trimmed.ToLowerInvariant();
    }

    private static string BuildBackupPath(string target, Guid downloadId)
    {
        return $"{target}.magpie-replace-{downloadId:N}.orig";
    }

    private static string? FindPrimary(string sidecarPath, IReadOnlyList<string> primaryPaths)
    {
        if (string.IsNullOrWhiteSpace(sidecarPath))
        {
            return null;
        }

        if (primaryPaths is null || primaryPaths.Count == 0)
        {
            return null;
        }

        var sidecarBase = StripExtensionAndLanguage(sidecarPath);
        if (string.IsNullOrEmpty(sidecarBase))
        {
            return null;
        }

        foreach (var primary in primaryPaths)
        {
            if (string.IsNullOrWhiteSpace(primary))
            {
                continue;
            }

            var primaryBase = Path.GetFileNameWithoutExtension(primary);
            if (string.Equals(primaryBase, sidecarBase, StringComparison.OrdinalIgnoreCase))
            {
                return primary;
            }
        }

        return null;
    }

    private static string StripExtensionAndLanguage(string path)
    {
        var fileName = Path.GetFileName(path);
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(withoutExtension))
        {
            return withoutExtension;
        }

        var match = LanguageSuffixPattern.Match(withoutExtension);
        if (!match.Success)
        {
            return withoutExtension;
        }

        var langGroup = match.Groups["lang"].Value;
        if (!IsRecognizedLanguageTag(langGroup))
        {
            return withoutExtension;
        }

        return match.Groups["base"].Value;
    }

    private static bool IsRecognizedLanguageTag(string tag)
    {
        var dashIndex = tag.IndexOf('-', StringComparison.Ordinal);
        if (dashIndex > 0)
        {
            var language = tag[..dashIndex];
            return ContainsIgnoreCase(TwoLetterLanguageCodes, language);
        }

        if (tag.Length == 2)
        {
            return ContainsIgnoreCase(TwoLetterLanguageCodes, tag);
        }

        if (tag.Length == 3)
        {
            return ContainsIgnoreCase(ThreeLetterLanguageCodes, tag);
        }

        return false;
    }

    private static bool ContainsIgnoreCase(IReadOnlyList<string> source, string value)
    {
        foreach (var entry in source)
        {
            if (string.Equals(entry, value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        if (!TryResolveDownloadId(context, out var rawDownloadId, out var source))
        {
            throw new ArgumentException("A download ID is required.");
        }

        if (!Guid.TryParse(rawDownloadId, out var downloadId))
        {
            throw new ArgumentException(source is DownloadIdSource.EventContext
                ? "The job received a download ID with an invalid format."
                : "The download ID has an invalid format.");
        }

        var dryRun = false;
        if (context.Parameters.TryGetValue(MagpieJobs.Parameters.DryRun, out var rawDryRun) &&
            !string.IsNullOrWhiteSpace(rawDryRun))
        {
            if (!bool.TryParse(rawDryRun, out dryRun))
            {
                throw new ArgumentException("Dry run must be true or false.");
            }
        }

        ImportCollisionMode? collisionMode = null;
        if (context.Parameters.TryGetValue(MagpieJobs.Parameters.CollisionMode, out var rawCollisionMode) &&
            !string.IsNullOrWhiteSpace(rawCollisionMode))
        {
            if (!Enum.TryParse<ImportCollisionMode>(rawCollisionMode, ignoreCase: true, out var parsed))
            {
                throw new ArgumentException("Collision mode must be \"fail\", \"skip\", or \"replace\".");
            }

            collisionMode = parsed;
        }

        var userId = context.ManualTriggerUserId;
        var correlationId = context.CorrelationId;

        using var activity = DownloadTelemetry.Source.StartActivity("DownloadImport");
        activity?.SetTag("magpie.download.id", downloadId.ToString());
        activity?.SetTag("magpie.import.dry_run", dryRun);

        progressReporter.Report(0, "The import will load the download.");

        var download = await downloadRepository.GetByIdAsync(downloadId, cancellationToken)
            .ConfigureAwait(false);
        if (download is null)
        {
            throw new InvalidOperationException($"Download {downloadId} does not exist.");
        }

        if (download.Status == DownloadStatus.Imported)
        {
            progressReporter.Report(1, "The import completed.");
            return;
        }

        if (download.Status is not (DownloadStatus.Downloaded or DownloadStatus.Importing))
        {
            throw new InvalidOperationException(
                $"Download {downloadId} is in status {download.Status} and cannot be imported");
        }

        if (!dryRun)
        {
            await downloadRepository
                .MarkImportingAsync(downloadId, timeProvider.GetUtcNow().UtcDateTime, userId, cancellationToken)
                .ConfigureAwait(false);
        }

        var dryRunTag = new KeyValuePair<string, object?>("dry_run", dryRun);
        try
        {
            await ImportCoreAsync(
                download,
                userId,
                correlationId,
                dryRun,
                collisionMode,
                progressReporter,
                activity,
                cancellationToken)
                .ConfigureAwait(false);
            DownloadTelemetry.Imports.Add(1, new("outcome", "success"), dryRunTag);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DownloadFileSafetyException)
        {
            DownloadTelemetry.Imports.Add(1, new("outcome", "failure"), dryRunTag);
            throw;
        }
        catch (Exception ex) when (!dryRun)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("magpie.download.failure_reason", nameof(DownloadFailureReason.ImportFailed));
            DownloadTelemetry.Imports.Add(1, new("outcome", "failure"), dryRunTag);
            await MarkImportFailedAsync(download, userId, correlationId, ex, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task ImportCoreAsync(
        Download download,
        Guid? userId,
        Guid? correlationId,
        bool dryRun,
        ImportCollisionMode? collisionMode,
        IProgressReporter progress,
        Activity? importActivity,
        CancellationToken cancellationToken)
    {
        var downloadId = download.Id;

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(null, "The import will load the content hierarchy.");

        ContentItem contentItem;
        IReadOnlyList<ContentHierarchyItem> ancestors;
        Release release;
        using (DownloadTelemetry.Source.StartActivity("ResolveChain"))
        {
            (contentItem, ancestors, release) = await ResolveChainAsync(
                download, userId, correlationId, cancellationToken)
                .ConfigureAwait(false);
        }

        var transferMode = ResolveTransferMode(release.Protocol);
        importActivity?.SetTag("magpie.download.protocol", release.Protocol);
        importActivity?.SetTag("magpie.import.transfer_mode", TransferModeName(transferMode));

        var hierarchyItem = ancestors[0];
        var resolvedRootFolderId = hierarchyItem.EffectiveRootFolderId;
        logger.LogInformation(
            "The job will use root folder {RootFolderId} for download {DownloadId} and content hierarchy item " +
            "{ContentHierarchyItemId}",
            resolvedRootFolderId,
            downloadId,
            hierarchyItem.Id);

        var rootFolder = await rootFolderRepository.GetByIdAsync(resolvedRootFolderId, cancellationToken)
            .ConfigureAwait(false);
        if (rootFolder is null)
        {
            throw new InvalidOperationException(
                $"Content hierarchy item {hierarchyItem.Id} references missing root folder " +
                $"{resolvedRootFolderId}.");
        }

        var effectiveCollisionMode = collisionMode ?? rootFolder.DefaultImportCollisionMode;

        var operation = await BuildImportOperationAsync(
            download,
            hierarchyItem,
            effectiveCollisionMode,
            dryRun,
            userId,
            correlationId,
            progress,
            importActivity,
            cancellationToken)
            .ConfigureAwait(false);

        if (dryRun)
        {
            await WriteDryRunPlanAsync(
                download,
                operation.PlannedOperations,
                transferMode,
                userId,
                correlationId,
                cancellationToken)
                .ConfigureAwait(false);
            progress.Report(
                1,
                $"Dry run: {TransferModeName(transferMode)} {operation.PlannedOperations.Count} file(s). " +
                "The audit history of the download holds the plan.");
            return;
        }

        var acquisitionScopes = await downloadScopeRepository.QueryAllAsync(
            new DownloadScopeQuery { DownloadId = downloadId },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var acquisitionScopeNodeIds = acquisitionScopes
            .Select(scope => scope.ContentHierarchyItemId)
            .Distinct()
            .ToList();
        var acquisitionLeaves = await ResolveScopedLeavesAsync(acquisitionScopeNodeIds, cancellationToken)
            .ConfigureAwait(false);
        var replaceSnapshots = await CaptureReplaceSnapshotsAsync(operation.ReplaceTargets, cancellationToken)
            .ConfigureAwait(false);

        var transferResult = Transfer(
            transferMode,
            operation.FilteredOperations,
            operation.ReplaceTargets,
            downloadId,
            progress,
            cancellationToken);
        var transferred = transferResult.Transferred;
        var grabTargetNode = ancestors[0];
        var root = ancestors[^1];
        var persisted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(null, "The import will save the media files.");

            var leaves = await WidenWithOpportunisticLeavesAsync(
                release,
                grabTargetNode,
                acquisitionLeaves,
                cancellationToken)
                .ConfigureAwait(false);
            var groups = await BuildLeafImportGroupsAsync(
                release,
                grabTargetNode,
                contentItem,
                leaves,
                transferred,
                userId,
                correlationId,
                cancellationToken)
                .ConfigureAwait(false);

            var importedMainContentLeafIds = groups
                .Where(group => group.Files.Any(file => file.Operation.IsMainContent))
                .Select(group => group.ContentItem.ContentHierarchyItemId)
                .ToHashSet();
            var uncoveredLeafIds = acquisitionLeaves
                .Select(leaf => leaf.Id)
                .Where(leafId => !importedMainContentLeafIds.Contains(leafId))
                .ToHashSet();
            var reconciledDownloadScopeNodeIds = acquisitionScopeNodeIds
                .Where(nodeId => !uncoveredLeafIds.Contains(nodeId))
                .ToList();
            await PersistAndPublishAsync(
                download,
                contentItem,
                release,
                groups,
                reconciledDownloadScopeNodeIds,
                replaceSnapshots,
                rootFolder.UpgradeFileHandling,
                userId,
                correlationId,
                () => persisted = true,
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (persisted)
            {
                DiscardReplaceBackups(transferResult.ReplaceBackups, downloadId);
            }
            else
            {
                RevertTransfers(transferred, downloadId);
                RestoreReplaceTargets(transferResult.ReplaceBackups, downloadId);
            }

            throw;
        }

        DiscardReplaceBackups(transferResult.ReplaceBackups, downloadId);

        await eventBus.PublishAsync(
            ContentEventBuilder.BuildContentImportedEvent(
                new()
                {
                    HierarchyItemId = contentItem.ContentHierarchyItemId,
                    ItemId = contentItem.Id,
                    ParentId = grabTargetNode.ParentId,
                    AncestorIds = [.. ancestors.Skip(1).Select(a => a.Id)],
                    RootId = root.Id,
                    ContentType = grabTargetNode.ContentType,
                    Level = grabTargetNode.Level,
                    MetadataContainerId = grabTargetNode.MetadataContainerId,
                    TopLevel = grabTargetNode.ParentId is null,
                    RootFolderId = grabTargetNode.RootFolderId,
                    QualityTierListId = grabTargetNode.QualityTierListId,
                    Monitoring = grabTargetNode.EffectiveMonitoring,
                    Fulfillment = contentItem.Fulfillment.ToString(),
                    WishlistId = contentItem.RequestedViaWishlistId
                },
                userId,
                transferred.Count) with
            {
                CorrelationId = correlationId
            },
            cancellationToken)
            .ConfigureAwait(false);

        progress.Report(1, "The import completed.");
    }

    private async Task<(ContentItem ContentItem, IReadOnlyList<ContentHierarchyItem> Ancestors, Release Release)>
        ResolveChainAsync(Download download, Guid? userId, Guid? correlationId, CancellationToken cancellationToken)
    {
        if (download.ReleaseId is null)
        {
            throw new InvalidOperationException(
                $"Download {download.Id} has no associated release and cannot be imported");
        }

        var release = await releaseRepository
            .GetByIdAsync(download.ReleaseId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (release is null)
        {
            throw new InvalidOperationException(
                $"Download {download.Id} references missing release {download.ReleaseId}.");
        }

        var targetHierarchyItemId = await ResolveImportTargetNodeAsync(download.Id, cancellationToken)
            .ConfigureAwait(false);
        if (targetHierarchyItemId is null)
        {
            throw new InvalidOperationException(
                $"Download {download.Id} has no release scope identifying an import target for release {release.Id}");
        }

        var contentItem = await contentService.GetOrCreateContentItemAsync(
            targetHierarchyItemId.Value,
            timeProvider.GetUtcNow().UtcDateTime,
            userId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var chain = await contentHierarchyItemRepository
            .GetLineageAsync(contentItem.ContentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (chain.Count == 0)
        {
            throw new InvalidOperationException(
                $"Content item {contentItem.Id} references missing hierarchy item " +
                $"{contentItem.ContentHierarchyItemId}.");
        }

        return (contentItem, chain, release);
    }

    private async Task<Guid?> ResolveImportTargetNodeAsync(Guid downloadId, CancellationToken cancellationToken)
    {
        var downloadScopes = await downloadScopeRepository.QueryAllAsync(
            new DownloadScopeQuery { DownloadId = downloadId },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var nodeIds = downloadScopes.Select(scope => scope.ContentHierarchyItemId).Distinct().ToList();
        var paths = new List<IReadOnlyList<Guid>>();
        foreach (var nodeId in nodeIds.Distinct())
        {
            var lineage = await contentHierarchyItemRepository.GetLineageAsync(nodeId, cancellationToken)
                .ConfigureAwait(false);
            if (lineage.Count > 0)
            {
                paths.Add([.. lineage.Reverse().Select(node => node.Id)]);
            }
        }

        if (paths.Count == 0 || paths.Any(path => path[0] != paths[0][0]))
        {
            return null;
        }

        var commonDepth = 0;
        for (var depth = 1; depth < paths.Min(path => path.Count); depth++)
        {
            if (!paths.All(path => path[depth] == paths[0][depth]))
            {
                break;
            }

            commonDepth = depth;
        }

        return paths[0][commonDepth];
    }

    private async Task<IReadOnlyList<ContentHierarchyItem>> ResolveScopedLeavesAsync(
        IReadOnlyList<Guid> scopeNodeIds,
        CancellationToken cancellationToken)
    {
        if (scopeNodeIds.Count == 0)
        {
            return [];
        }

        var scopedNodes = new List<ContentHierarchyItem>();
        var ancestorIds = new HashSet<Guid>();
        foreach (var nodeId in scopeNodeIds.Distinct())
        {
            var lineage = await contentHierarchyItemRepository
                .GetLineageAsync(nodeId, cancellationToken)
                .ConfigureAwait(false);
            if (lineage.Count == 0)
            {
                continue;
            }

            scopedNodes.Add(lineage[0]);
            for (var index = 1; index < lineage.Count; index++)
            {
                ancestorIds.Add(lineage[index].Id);
            }
        }

        return [.. scopedNodes.Where(node => !ancestorIds.Contains(node.Id))];
    }

    private async Task<IReadOnlyList<ContentHierarchyItem>> WidenWithOpportunisticLeavesAsync(
        Release release,
        ContentHierarchyItem importTargetNode,
        IReadOnlyList<ContentHierarchyItem> acquisitionLeaves,
        CancellationToken cancellationToken)
    {
        var acquisitionLeafIds = acquisitionLeaves.Select(leaf => leaf.Id).ToHashSet();
        var scope = await releaseEvaluationService
            .ResolveScopeAsync(release, [], importTargetNode.Id, cancellationToken)
            .ConfigureAwait(false);
        var opportunisticLeafIds = scope.ActionableNodeIds
            .Where(nodeId => !acquisitionLeafIds.Contains(nodeId))
            .ToList();
        if (opportunisticLeafIds.Count == 0)
        {
            return acquisitionLeaves;
        }

        var leaves = new List<ContentHierarchyItem>(acquisitionLeaves);
        foreach (var leafId in opportunisticLeafIds)
        {
            var leaf = await contentHierarchyItemRepository.GetByIdAsync(leafId, cancellationToken)
                .ConfigureAwait(false);
            if (leaf?.EffectiveMonitoring != true)
            {
                continue;
            }

            var contentItem = leaf.ContentItem ?? (await contentRepository.QueryAsync(
                new ContentItemQuery { ContentHierarchyItemId = leaf.Id },
                new() { PageNumber = 1, PageSize = 2 },
                null,
                cancellationToken).ConfigureAwait(false)).Items.SingleOrDefault();

            if (contentItem is null || contentItem.ActiveAcquisitionStatus is not null)
            {
                continue;
            }

            if (await ReleaseImprovesLeafAsync(release, leaf, contentItem, cancellationToken).ConfigureAwait(false))
            {
                leaves.Add(leaf);
            }
        }

        return leaves;
    }

    private async Task<bool> ReleaseImprovesLeafAsync(
        Release release,
        ContentHierarchyItem leaf,
        ContentItem contentItem,
        CancellationToken cancellationToken)
    {
        if (contentItem.Fulfillment is not FulfillmentStatus.Missing and not FulfillmentStatus.Upgradable)
        {
            return false;
        }

        var assessment = await releaseEvaluationService.AssessForContentAsync(release, [], leaf.Id, cancellationToken)
            .ConfigureAwait(false);
        if (assessment.IsAcceptable is not true)
        {
            return false;
        }

        return true;
    }

    private async Task<ImportOperation> BuildImportOperationAsync(
        Download download,
        ContentHierarchyItem contentHierarchyItem,
        ImportCollisionMode effectiveCollisionMode,
        bool dryRun,
        Guid? userId,
        Guid? correlationId,
        IProgressReporter progress,
        Activity? importActivity,
        CancellationToken cancellationToken)
    {
        var downloadId = download.Id;

        using var activity = DownloadTelemetry.Source.StartActivity("BuildImportOperation");

        if (string.IsNullOrWhiteSpace(download.OutputPath))
        {
            throw new InvalidOperationException($"Download {downloadId} has no OutputPath and cannot be imported");
        }

        var importSourcePath = await ResolveImportSourcePathAsync(download, cancellationToken)
            .ConfigureAwait(false);

        var sourceIsDirectory = fileSystem.DirectoryExists(importSourcePath);
        if (!sourceIsDirectory && !fileSystem.FileExists(importSourcePath))
        {
            throw new InvalidOperationException(
                string.Equals(importSourcePath, download.OutputPath, StringComparison.Ordinal)
                    ? $"Download {downloadId} OutputPath {download.OutputPath} does not exist"
                    : $"Download {downloadId} OutputPath {download.OutputPath} (resolved via remote path " +
                        $"mapping to {importSourcePath}) does not exist");
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(null, "The import will list the source files.");

        var sourceFiles = sourceIsDirectory
            ? fileSystem.EnumerateFiles(importSourcePath).ToArray()
            : [importSourcePath];

        var contentTypeSlug = contentHierarchyItem.ContentType;
        var contentTypeDefinition = ResolveContentTypeDefinition(contentTypeSlug);
        var acceptedLevels = GetLevelsAtOrUnder(contentTypeDefinition, contentHierarchyItem.Level);
        var acceptedFileTypes = acceptedLevels.SelectMany(level => level.AcceptedFileTypes).ToArray();
        var acceptedSidecarTypes = acceptedLevels.SelectMany(level => level.AcceptedSidecarTypes).ToArray();

        var blockedFileTypes = await blockedFileTypeRepository
            .ListAllAsync(cancellationToken)
            .ConfigureAwait(false);
        var blockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blockedMimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var blocked in blockedFileTypes)
        {
            if (blocked.Pattern.Contains('/', StringComparison.Ordinal))
            {
                blockedMimes.Add(blocked.Pattern);
            }
            else
            {
                blockedExtensions.Add(NormaliseBlockedExtension(blocked.Pattern));
            }
        }

        var primaryCandidates = new List<string>(sourceFiles.Length);
        var sidecarCandidates = new List<string>(sourceFiles.Length);
        foreach (var source in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (HasSampleDirectorySegment(source))
            {
                continue;
            }

            var blockedPattern = await MatchBlockedFileTypeAsync(
                source, blockedExtensions, blockedMimes, cancellationToken)
                .ConfigureAwait(false);
            if (blockedPattern is not null)
            {
                var patternTag = new KeyValuePair<string, object?>("pattern", blockedPattern);
                DownloadTelemetry.ImportBlockedFiles.Add(1, patternTag);
                activity?.SetTag("magpie.import.blocked_count", 1);
                SetFailureReason(activity, importActivity, DownloadFailureReason.BlockedFileType);
                if (!dryRun)
                {
                    await MarkDownloadFailedAsync(
                        download,
                        DownloadFailureReason.BlockedFileType,
                        $"Blocked by file-type deny-list: {blockedPattern}",
                        userId,
                        correlationId,
                        cancellationToken)
                        .ConfigureAwait(false);
                }

                throw new DownloadFileSafetyException(
                    $"Download {downloadId} contains a blocked file type (pattern {blockedPattern}): {source}.");
            }

            if (await fileSystem
                    .MatchesAnyFileTypeAsync(source, acceptedFileTypes, cancellationToken)
                    .ConfigureAwait(false))
            {
                primaryCandidates.Add(source);
                continue;
            }

            if (await fileSystem
                    .MatchesAnyFileTypeAsync(source, acceptedSidecarTypes, cancellationToken)
                    .ConfigureAwait(false))
            {
                sidecarCandidates.Add(source);
                continue;
            }

            if (IsCommonIgnoredSidecar(source))
            {
                continue;
            }

            logger.LogDebug(
                "The job skipped file {FileName} for download {DownloadId} because it is not accepted media" +
                " or a sidecar",
                Path.GetFileName(source),
                downloadId);
        }

        var pairedSidecars = new List<string>(sidecarCandidates.Count);
        if (primaryCandidates.Count > 0 && sidecarCandidates.Count > 0)
        {
            foreach (var sidecar in sidecarCandidates)
            {
                var match = FindPrimary(sidecar, primaryCandidates);
                if (match is not null)
                {
                    pairedSidecars.Add(sidecar);
                }
            }
        }

        activity?.SetTag("magpie.import.primary_file_count", primaryCandidates.Count);
        activity?.SetTag("magpie.import.sidecar_count", pairedSidecars.Count);

        if (primaryCandidates.Count == 0)
        {
            SetFailureReason(activity, importActivity, DownloadFailureReason.NoUsableContent);
            if (!dryRun)
            {
                await MarkDownloadFailedAsync(
                    download,
                    DownloadFailureReason.NoUsableContent,
                    "No usable content: the download contained no files matching " +
                    $"the accepted file types for content type {contentTypeSlug ?? "(unknown)"}.",
                    userId,
                    correlationId,
                    cancellationToken)
                    .ConfigureAwait(false);
            }

            throw new DownloadFileSafetyException(
                $"Download {downloadId} produced no accepted primary files for content type " +
                $"{contentTypeSlug ?? "(unknown)"}. " +
                "The download has been failed with reason NoUsableContent.");
        }

        var acceptedFiles = new List<string>(primaryCandidates.Count + pairedSidecars.Count);
        var filePathComparer = StringComparer.Create(
            CultureInfo.InvariantCulture,
            CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

        acceptedFiles.AddRange(primaryCandidates.OrderBy(path => path, filePathComparer));
        acceptedFiles.AddRange(pairedSidecars.OrderBy(path => path, filePathComparer));

        var primaryPaths = primaryCandidates.ToHashSet(PathComparer);
        var plannedOps = new List<ImportFileOperation>(acceptedFiles.Count);

        foreach (var source in acceptedFiles)
        {
            var preview = await contentFileService
                .PreviewRenameAsync(contentHierarchyItem.Id, new(source), cancellationToken)
                .ConfigureAwait(false);

            plannedOps.Add(new ImportFileOperation(
                source,
                preview.Rename.FullName,
                await ResolveFileKindAsync(source, cancellationToken).ConfigureAwait(false),
                primaryPaths.Contains(source)));
        }

        plannedOps = RemoveDuplicatePlannedOperations(plannedOps, downloadId);

        if (dryRun)
        {
            return new ImportOperation
            {
                PlannedOperations = plannedOps,
                FilteredOperations = [],
                ReplaceTargets = []
            };
        }

        var replaceTargets = new List<string>();
        var filteredOps = new List<ImportFileOperation>(plannedOps.Count);
        foreach (var op in plannedOps)
        {
            if (!fileSystem.FileExists(op.Destination))
            {
                filteredOps.Add(op);
                continue;
            }

            switch (effectiveCollisionMode)
            {
                case ImportCollisionMode.Fail:
                    throw new InvalidOperationException(
                        $"Destination file {op.Destination} already exists. The job cannot import download " +
                        $"{downloadId}");
                case ImportCollisionMode.Skip:
                    logger.LogDebug(
                        "The job skipped an operation for download {DownloadId} because destination " +
                        "{DestinationPath} exists and the mode is Skip",
                        downloadId,
                        op.Destination);
                    continue;
                case ImportCollisionMode.Replace:
                    replaceTargets.Add(op.Destination);
                    filteredOps.Add(op);
                    continue;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported collision mode {effectiveCollisionMode} for download {downloadId}");
            }
        }

        if (filteredOps.Count == 0)
        {
            logger.LogWarning(
                "Download {DownloadId} has no operations after the collision filter, so the job will mark it " +
                "Failed/DownloadStuck",
                downloadId);
            SetFailureReason(activity, importActivity, DownloadFailureReason.DownloadStuck);
            await MarkDownloadFailedAsync(
                download,
                DownloadFailureReason.DownloadStuck,
                "Every planned file exists at its destination. The job imported no files.",
                userId,
                correlationId,
                cancellationToken)
                .ConfigureAwait(false);
            throw new DownloadFileSafetyException(
                $"Download {downloadId} imported no files: every planned operation collided under " +
                "Skip collision mode. The download has been failed with reason DownloadStuck.");
        }

        return new ImportOperation
        {
            PlannedOperations = plannedOps,
            FilteredOperations = filteredOps,
            ReplaceTargets = replaceTargets
        };
    }

    private List<ImportFileOperation> RemoveDuplicatePlannedOperations(
        IReadOnlyList<ImportFileOperation> plannedOps,
        Guid downloadId)
    {
        var seen = new HashSet<string>(PathComparer);
        var filtered = new List<ImportFileOperation>(plannedOps.Count);
        foreach (var op in plannedOps)
        {
            if (seen.Add(op.Destination))
            {
                filtered.Add(op);
                continue;
            }

            logger.LogWarning(
                "The job skipped an operation for download {DownloadId} because source {SourcePath} duplicates " +
                "the planned destination {DestinationPath}",
                downloadId,
                op.Source,
                op.Destination);
        }

        return filtered;
    }

    private async Task<string> ResolveImportSourcePathAsync(Download download, CancellationToken cancellationToken)
    {
        var remotePath = download.OutputPath!;

        var mappings = await remotePathMappingRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var mappingList = mappings as IReadOnlyList<RemotePathMapping> ?? [.. mappings];
        if (mappingList.Count == 0)
        {
            return remotePath;
        }

        var downloadClient = await downloadClientRepository
            .GetByIdAsync(download.DownloadClientId, cancellationToken)
            .ConfigureAwait(false);

        var host = remotePathMappingService.GetHost(downloadClient?.Settings);
        var resolved = remotePathMappingService.Map(remotePath, host, mappingList);

        if (!string.Equals(resolved, remotePath, StringComparison.Ordinal))
        {
            logger.LogInformation(
                "The remote path mapping translated download {DownloadId} path {RemotePath} -> " +
                "{LocalPath} (client host {Host})",
                download.Id,
                remotePath,
                resolved,
                host);
        }

        return resolved;
    }

    private async Task<IReadOnlyDictionary<string, ReplaceSnapshot>> CaptureReplaceSnapshotsAsync(
        IReadOnlyList<string> replaceTargets,
        CancellationToken cancellationToken)
    {
        var snapshots = new Dictionary<string, ReplaceSnapshot>(PathComparer);
        foreach (var target in replaceTargets)
        {
            var matched = await mediaFileRepository.GetByPathAsync(target, cancellationToken)
                .ConfigureAwait(false);
            snapshots[target] = new ReplaceSnapshot(matched, ReadFileSize(target));
        }

        return snapshots;
    }

    private TransferResult Transfer(
        ImportMode transferMode,
        IReadOnlyList<ImportFileOperation> plannedOps,
        IReadOnlyList<string> replaceTargets,
        Guid downloadId,
        IProgressReporter progress,
        CancellationToken cancellationToken)
    {
        using var activity = DownloadTelemetry.Source.StartActivity("Transfer");
        activity?.SetTag("magpie.import.file_count", plannedOps.Count);
        activity?.SetTag("magpie.import.transfer_mode", TransferModeName(transferMode));

        var backups = StashReplaceTargets(replaceTargets, downloadId);

        var transferred = new List<TransferredOperation>(plannedOps.Count);
        var failingOp = default(ImportFileOperation);
        try
        {
            for (var i = 0; i < plannedOps.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var op = plannedOps[i];
                failingOp = op;
                progress.ReportFraction(
                    i,
                    plannedOps.Count,
                    $"The import will process {Path.GetFileName(op.Source)}.");
                activity?.AddEvent(new ActivityEvent(
                    "file.transfer",
                    tags: new ActivityTagsCollection
                    {
                        ["magpie.import.file"] = Path.GetFileName(op.Source),
                    }));

                var destDir = Path.GetDirectoryName(op.Destination);
                if (!string.IsNullOrEmpty(destDir))
                {
                    fileSystem.CreateDirectory(destDir!);
                }

                ExecuteTransfer(transferMode, op.Source, op.Destination);
                transferred.Add(new TransferredOperation(op, transferMode));
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            RevertTransfers(transferred, downloadId);
            RestoreReplaceTargets(backups, downloadId);
            RecordTransferOutcome(activity, transferred.Count, "reverted");
            throw;
        }
        catch (Exception ex)
        {
            RecordTransferOutcome(activity, transferred.Count, "failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(
                ex,
                "The job failed to {TransferMode} {SourcePath} to {DestinationPath} for download {DownloadId}",
                transferMode,
                failingOp.Source,
                failingOp.Destination,
                downloadId);

            var reverted = RevertTransfers(transferred, downloadId);
            var restored = RestoreReplaceTargets(backups, downloadId);

            throw new InvalidOperationException(
                $"Import of download {downloadId} failed after transferring {transferred.Count} file(s). " +
                $"Reverted {reverted} file(s) and restored {restored} pre-existing file(s). " +
                $"Failing transfer: {failingOp.Source} -> {failingOp.Destination}. {ex.Message}",
                ex);
        }

        RecordTransferOutcome(activity, transferred.Count, "success");
        return new TransferResult(transferred, backups);
    }

    private List<ReplaceBackup> StashReplaceTargets(IReadOnlyList<string> replaceTargets, Guid downloadId)
    {
        var backups = new List<ReplaceBackup>(replaceTargets.Count);
        foreach (var target in replaceTargets)
        {
            var backupPath = BuildBackupPath(target, downloadId);
            try
            {
                fileSystem.Move(target, backupPath);
                backups.Add(new ReplaceBackup(target, backupPath));
            }
            catch (Exception ex)
            {
                RestoreReplaceTargets(backups, downloadId);
                throw new InvalidOperationException(
                    $"Replace mode for download {downloadId} failed to stash pre-existing target {target} " +
                    $"before transfer. {ex.Message}",
                    ex);
            }
        }

        return backups;
    }

    private int RestoreReplaceTargets(IReadOnlyList<ReplaceBackup> backups, Guid downloadId)
    {
        var restored = 0;
        for (var i = backups.Count - 1; i >= 0; i--)
        {
            var backup = backups[i];
            try
            {
                if (fileSystem.FileExists(backup.OriginalPath))
                {
                    fileSystem.DeleteFile(backup.OriginalPath);
                }

                fileSystem.Move(backup.BackupPath, backup.OriginalPath);
                restored++;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "The rollback failed to restore {OriginalPath} from {BackupPath} for download {DownloadId}",
                    backup.OriginalPath,
                    backup.BackupPath,
                    downloadId);
            }
        }

        return restored;
    }

    private void DiscardReplaceBackups(IReadOnlyList<ReplaceBackup> backups, Guid downloadId)
    {
        foreach (var backup in backups)
        {
            try
            {
                fileSystem.DeleteFile(backup.BackupPath);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "The job failed to delete the stale backup {BackupPath} of the already-imported download " +
                    "{DownloadId}",
                    backup.BackupPath,
                    downloadId);
            }
        }
    }

    private void ExecuteTransfer(ImportMode mode, string source, string destination)
    {
        switch (mode)
        {
            case ImportMode.Copy:
                fileSystem.Copy(source, destination);
                break;
            case ImportMode.Move:
                fileSystem.Move(source, destination);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(mode),
                    mode,
                    "The import pipeline only produces Move or Copy transfers.");
        }
    }

    private int RevertTransfers(IReadOnlyList<TransferredOperation> transferred, Guid downloadId)
    {
        var reverted = 0;
        for (var i = transferred.Count - 1; i >= 0; i--)
        {
            var entry = transferred[i];
            var op = entry.Operation;
            try
            {
                if (entry.ExecutedMode == ImportMode.Copy)
                {
                    fileSystem.DeleteFile(op.Destination);
                }
                else
                {
                    fileSystem.Move(op.Destination, op.Source);
                }

                reverted++;
            }
            catch (Exception revertEx)
            {
                logger.LogError(
                    revertEx,
                    "The rollback failed to revert {DestinationPath} ({TransferMode}) for download {DownloadId}",
                    op.Destination,
                    entry.ExecutedMode,
                    downloadId);
            }
        }

        return reverted;
    }

    private async Task<IReadOnlyList<LeafImportGroup>> BuildLeafImportGroupsAsync(
        Release release,
        ContentHierarchyItem grabTargetNode,
        ContentItem grabTargetContentItem,
        IReadOnlyList<ContentHierarchyItem> leaves,
        IReadOnlyList<TransferredOperation> transferred,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var grabTargetIsSoleLeaf = leaves.Count == 0 || (leaves.Count == 1 && leaves[0].Id == grabTargetNode.Id);
        if (grabTargetIsSoleLeaf)
        {
            return [new LeafImportGroup(grabTargetContentItem, transferred, leaves.Count == 0)];
        }

        var bindingByKey = await BindFilesToLeavesAsync(
            release,
            grabTargetNode,
            leaves,
            transferred,
            cancellationToken)
            .ConfigureAwait(false);

        var filesByLeaf = new Dictionary<Guid, List<TransferredOperation>>();
        var unbound = new List<TransferredOperation>();
        foreach (var entry in transferred)
        {
            if (bindingByKey.TryGetValue(entry.Operation.Destination, out var leafId))
            {
                if (!filesByLeaf.TryGetValue(leafId, out var files))
                {
                    files = [];
                    filesByLeaf[leafId] = files;
                }

                files.Add(entry);
            }
            else
            {
                unbound.Add(entry);
            }
        }

        var groups = new List<LeafImportGroup>(filesByLeaf.Count + 1);
        foreach (var (leafId, files) in filesByLeaf)
        {
            var leafContentItem = await contentService.GetOrCreateContentItemAsync(
                leafId,
                timeProvider.GetUtcNow().UtcDateTime,
                userId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            groups.Add(new LeafImportGroup(leafContentItem, files, false));
        }

        if (unbound.Count > 0)
        {
            logger.LogWarning(
                "Release {ReleaseId} has {UnboundCount} of {TotalCount} file(s) unbound to a satisfied leaf, so " +
                "the job will attach them to container {ContentItemId}: {UnboundFiles}",
                release.Id,
                unbound.Count,
                transferred.Count,
                grabTargetContentItem.Id,
                string.Join(", ", unbound.Select(u => u.Operation.Destination)));
            groups.Add(new LeafImportGroup(grabTargetContentItem, unbound, true));
        }

        return groups;
    }

    private async Task PersistAndPublishAsync(
        Download download,
        ContentItem primaryContentItem,
        Release release,
        IReadOnlyList<LeafImportGroup> groups,
        IReadOnlyCollection<Guid> reconciledDownloadScopeNodeIds,
        IReadOnlyDictionary<string, ReplaceSnapshot> replaceSnapshots,
        UpgradeFileHandling upgradeFileHandling,
        Guid? userId,
        Guid? correlationId,
        Action onPersisted,
        CancellationToken cancellationToken)
    {
        var downloadId = download.Id;
        var totalFiles = groups.Sum(g => g.Files.Count);

        var resolvedGroups = new List<ResolvedLeafImport>(groups.Count);
        foreach (var group in groups)
        {
            var qualityStatus = group.Files.Any(file => file.Operation.IsMainContent)
                ? await ResolveImportedQualityStatusAsync(release, group.ContentItem, downloadId, cancellationToken)
                    .ConfigureAwait(false)
                : group.ContentItem.QualityStatus;
            resolvedGroups.Add(new ResolvedLeafImport(
                group.ContentItem,
                group.Files,
                group.UnresolvedContainerBinding,
                group.ContentItem.Fulfillment,
                qualityStatus));
        }

        try
        {
            await PersistImportAsync(
                download,
                primaryContentItem.ContentHierarchyItemId,
                resolvedGroups,
                reconciledDownloadScopeNodeIds,
                replaceSnapshots,
                upgradeFileHandling,
                userId,
                correlationId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The job failed to save the import for download {DownloadId} after transferring " +
                "{TransferredCount} file(s)",
                downloadId,
                totalFiles);
            throw;
        }

        onPersisted();
    }

    private async Task<QualityStatus?> ResolveImportedQualityStatusAsync(
        Release release,
        ContentItem contentItem,
        Guid downloadId,
        CancellationToken cancellationToken)
    {
        ReleaseAssessment? score;
        try
        {
            score = await releaseEvaluationService
                .ScoreForContentAsync(
                    release,
                    contentItem.ContentHierarchyItemId,
                    currentQualityOrdinal: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The job failed to score release {ReleaseId} for download {DownloadId}",
                release.Id,
                downloadId);
            return null;
        }

        if (score is null)
        {
            return null;
        }

        if (score.IsAcceptable != true || score.IsCutoffMet == true)
        {
            return QualityStatus.Met;
        }

        var node = await contentHierarchyItemRepository
            .GetByIdAsync(contentItem.ContentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        var tierList = node?.EffectiveQualityTierListId is { } tierListId
            ? await qualityService.GetTierListAsync(tierListId, cancellationToken).ConfigureAwait(false)
            : null;
        if (tierList is not null && tierList.UpgradeAllowed)
        {
            logger.LogInformation(
                "Release {ReleaseId} for download {DownloadId} scored below the cutoff, so content item " +
                "{ContentItemId} needs an upgrade",
                release.Id,
                downloadId,
                contentItem.Id);
            return QualityStatus.UpgradeWanted;
        }

        return QualityStatus.Met;
    }

    private async Task<IReadOnlyDictionary<string, Guid>> BindFilesToLeavesAsync(
        Release release,
        ContentHierarchyItem grabTargetNode,
        IReadOnlyList<ContentHierarchyItem> leaves,
        IReadOnlyList<TransferredOperation> transferred,
        CancellationToken cancellationToken)
    {
        var files = transferred
            .Select(t => new ReleaseFileRef(t.Operation.Destination, Path.GetFileName(t.Operation.Destination)))
            .ToArray();

        var assignments = await releaseEvaluationService
            .AssignFilesToLeavesAsync(release, grabTargetNode, leaves, files, cancellationToken)
            .ConfigureAwait(false);
        var leafIds = leaves.Select(l => l.Id).ToHashSet();

        var map = new Dictionary<string, Guid>(PathComparer);
        foreach (var assignment in assignments)
        {
            if (assignment.LeafContentHierarchyItemId is { } resolvedLeafId && leafIds.Contains(resolvedLeafId))
            {
                map[assignment.FileKey] = resolvedLeafId;
            }
        }

        return map;
    }

    private async Task PersistImportAsync(
        Download download,
        Guid primaryContentHierarchyItemId,
        IReadOnlyList<ResolvedLeafImport> groups,
        IReadOnlyCollection<Guid> reconciledDownloadScopeNodeIds,
        IReadOnlyDictionary<string, ReplaceSnapshot> replaceSnapshots,
        UpgradeFileHandling upgradeFileHandling,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var allFiles = groups.SelectMany(g => g.Files).ToList();
        var importedPaths = allFiles.Select(file => file.Operation.Destination).ToHashSet(PathComparer);
        var probes = await ProbeFilesAsync(
            [.. allFiles.Select(file => file.Operation.Destination)],
            cancellationToken).ConfigureAwait(false);

        var executedMode = allFiles.Count > 0 ? allFiles[0].ExecutedMode : ImportMode.Move;
        var transferVerb = allFiles.Count > 0 && executedMode == ImportMode.Copy
            ? "Copied"
            : "Moved";

        using var activity = DownloadTelemetry.Source.StartActivity("PersistImport");
        activity?.SetTag("magpie.import.leaf_group_count", groups.Count);

        var transferModeTag = new KeyValuePair<string, object?>("transfer_mode", TransferModeName(executedMode));
        DownloadTelemetry.ImportBytes.Record(probes.Values.Sum(probe => probe.FileSize), transferModeTag);

        await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var replacedCount = 0;
                var replaceDetailFragments = new List<string>();

                foreach (var group in groups)
                {
                    var contentItem = group.ContentItem;
                    var supersedePriorFiles = group.Prior == FulfillmentStatus.Upgradable &&
                        upgradeFileHandling == UpgradeFileHandling.Replace;
                    var priorMediaFiles = new List<MediaFile>();
                    if (supersedePriorFiles)
                    {
                        priorMediaFiles.AddRange(await mediaFileRepository.QueryAllAsync(
                            new() { ContentItemIds = [contentItem.Id] },
                            cancellationToken: ct).ConfigureAwait(false));
                    }

                    foreach (var entry in group.Files)
                    {
                        var op = entry.Operation;
                        var hadReplaceSnapshot = replaceSnapshots.TryGetValue(op.Destination, out var snapshot);
                        var probe = probes[op.Destination];

                        if (hadReplaceSnapshot && snapshot.ExistingMediaFile is { } prior)
                        {
                            await mediaFileRepository
                                .UpdateAsync(
                                    prior with
                                    {
                                        FileKind = probe.FileKind.ToString(),
                                        MimeType = probe.MimeType,
                                        FileSize = probe.FileSize,
                                        MediaInfo = null,
                                        ModifiedAt = now
                                    },
                                    userId,
                                    ct)
                                .ConfigureAwait(false);
                            replacedCount++;
                            var priorMediaInfo = prior.MediaInfo is null
                                ? "null"
                                : JsonSerializer.Serialize(prior.MediaInfo);
                            replaceDetailFragments.Add(
                                $"{op.Destination} (prior size: {snapshot.PriorSize}, prior media: {priorMediaInfo})");
                            continue;
                        }

                        await mediaFileRepository
                            .CreateAsync(
                                new MediaFile
                                {
                                    ContentItemId = contentItem.Id,
                                    FileKind = probe.FileKind.ToString(),
                                    MimeType = probe.MimeType,
                                    FilePath = probe.FilePath,
                                    FileSize = probe.FileSize,
                                    MediaInfo = null,
                                    CreatedAt = now,
                                    CreatedByUserId = userId,
                                },
                                userId,
                                ct)
                            .ConfigureAwait(false);

                        if (hadReplaceSnapshot)
                        {
                            replacedCount++;
                            replaceDetailFragments.Add(
                                $"{op.Destination} (prior size: {snapshot.PriorSize}, prior mediainfo: null)");
                        }
                    }

                    if (supersedePriorFiles)
                    {
                        var superseded = priorMediaFiles
                            .Where(m => !importedPaths.Contains(m.FilePath))
                            .ToList();
                        foreach (var prior in superseded)
                        {
                            await transactionManager.OnCommittedAsync(
                                _ =>
                                {
                                    try
                                    {
                                        if (fileSystem.FileExists(prior.FilePath))
                                        {
                                            fileSystem.DeleteFile(prior.FilePath);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogWarning(
                                            ex,
                                            "The job failed to delete superseded file {FilePath} for " +
                                            "{ContentItemId} after the database commit",
                                            prior.FilePath,
                                            contentItem.Id);
                                    }

                                    return Task.CompletedTask;
                                },
                                ct).ConfigureAwait(false);

                            await mediaFileRepository
                                .DeleteAsync(prior.Id, userId, ct)
                                .ConfigureAwait(false);

                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.Integrations,
                                    Operation = "SupersedeUpgrade",
                                    EntityId = download.Id.ToString(),
                                    EntityType = nameof(Download),
                                    Detail =
                                            $"Removed previous file {prior.FilePath} ({prior.FileSize}) " +
                                            $"after importing a quality upgrade for {download.Title}.",
                                    Timestamp = now,
                                    UserId = userId,
                                    CorrelationId = correlationId
                                },
                                ct).ConfigureAwait(false);
                        }
                    }

                    contentItem.QualityStatus = group.QualityStatus;
                    contentItem.ModifiedAt = now;
                    await contentRepository.UpdateAsync(contentItem, userId, ct).ConfigureAwait(false);

                    var fulfillmentChanged = await libraryImportService
                        .UpdateFulfillmentFromFilesAsync(contentItem.ContentHierarchyItemId, userId, ct)
                        .ConfigureAwait(false);
                    if (fulfillmentChanged)
                    {
                        var reconciledContentItem = (await contentRepository.QueryAsync(
                            new ContentItemQuery
                            {
                                ContentHierarchyItemId = contentItem.ContentHierarchyItemId
                            },
                            new() { PageNumber = 1, PageSize = 2 },
                            null,
                            ct).ConfigureAwait(false)).Items.SingleOrDefault()
                            ?? throw new InvalidOperationException($"Content item {contentItem.Id} does not exist.");
                        contentItem.Fulfillment = reconciledContentItem.Fulfillment;
                    }

                    await contentService
                        .MaterializeFulfillmentStatusAsync(
                            contentItem.ContentHierarchyItemId,
                            userId,
                            includeSelf: false,
                            ct)
                        .ConfigureAwait(false);

                    var fulfillment = group.ContentItem.Fulfillment;
                    var fulfillmentOperation = fulfillment switch
                    {
                        FulfillmentStatus.Fulfilled => "ImportFulfilled",
                        FulfillmentStatus.Upgradable => "ImportUpgradable",
                        FulfillmentStatus.FulfilledExternally => "ImportFulfilled",
                        _ => "ImportCompleted"
                    };
                    var fulfillmentDetail = group.Prior == fulfillment
                        ? $"Imported {group.Files.Count} file(s) for {download.Title} " +
                            $"(fulfillment unchanged: {fulfillment})."
                        : $"Imported {group.Files.Count} file(s) for {download.Title} " +
                            $"({group.Prior} -> {fulfillment}).";
                    await WriteActivityAuditAsync(
                        operation: fulfillmentOperation,
                        entityType: nameof(ContentItem),
                        entityId: group.ContentItem.Id.ToString(),
                        detail: fulfillmentDetail,
                        userId,
                        correlationId,
                        ct).ConfigureAwait(false);
                }

                var newDateCompleted = download.DateCompleted ?? now;
                await downloadRepository
                    .MarkImportedAsync(
                        download.Id,
                        primaryContentHierarchyItemId,
                        allFiles.Count,
                        newDateCompleted,
                        now,
                        userId,
                        ct)
                    .ConfigureAwait(false);

                await SetDownloadScopeAsync(download.Id, reconciledDownloadScopeNodeIds, userId, ct)
                    .ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(
                    new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Import",
                        EntityId = download.Id.ToString(),
                        EntityType = nameof(Download),
                        Detail = $"{transferVerb} {allFiles.Count} files for download {download.Title}",
                        Timestamp = now,
                        UserId = userId,
                        CorrelationId = correlationId
                    },
                    ct).ConfigureAwait(false);

                if (replacedCount > 0)
                {
                    var replacedFileCountLabel = replacedCount == 1
                        ? "1 media file"
                        : $"{replacedCount} media files";
                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "Replace",
                            EntityId = download.Id.ToString(),
                            EntityType = nameof(Download),
                            Detail = $"Replaced {replacedFileCountLabel}: {string.Join("; ", replaceDetailFragments)}.",
                            Timestamp = now,
                            UserId = userId,
                            CorrelationId = correlationId
                        },
                        ct).ConfigureAwait(false);
                }

                activity?.SetTag("magpie.import.replaced_count", replacedCount);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkImportFailedAsync(
        Download download,
        Guid? userId,
        Guid? correlationId,
        Exception cause,
        CancellationToken cancellationToken)
    {
        await MarkFailedCoreAsync(
            download,
            DownloadFailureReason.ImportFailed,
            cause.Message,
            "ImportFailed",
            $"Import failed for download {download.Title}: ",
            markEx => logger.LogWarning(
                markEx,
                "The job failed to mark download {DownloadId} as Failed/ImportFailed after an import error",
                download.Id),
            userId,
            correlationId,
            cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task MarkDownloadFailedAsync(
        Download download,
        DownloadFailureReason reason,
        string statusReason,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        await MarkFailedCoreAsync(
            download,
            reason,
            statusReason,
            "DownloadFailed",
            $"Download {download.Title} failed ({reason}): ",
            markEx => logger.LogWarning(
                markEx,
                "The job failed to mark download {DownloadId} as Failed ({Reason}) after a file-safety hit",
                download.Id,
                reason),
            userId,
            correlationId,
            cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task MarkFailedCoreAsync(
        Download download,
        DownloadFailureReason reason,
        string statusReason,
        string auditOperation,
        string auditDetailPrefix,
        Action<Exception> logMarkFailure,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var trimmedReason = statusReason.Length > 1024 ? statusReason[..1024] : statusReason;

        try
        {
            var marked = await downloadRepository.MarkFailedAsync(
                download.Id,
                reason,
                trimmedReason,
                auditOperation,
                auditDetailPrefix + trimmedReason,
                now,
                userId,
                correlationId,
                cancellationToken)
                .ConfigureAwait(false);
            if (!marked)
            {
                return;
            }
        }
        catch (Exception markEx)
        {
            logMarkFailure(markEx);
            return;
        }
    }

    private async Task WriteDryRunPlanAsync(
        Download download,
        IReadOnlyList<ImportFileOperation> plannedOperations,
        ImportMode transferMode,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var verb = TransferModeName(transferMode);
        await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                foreach (var planned in plannedOperations)
                {
                    await auditRecordRepository.CreateAsync(
                        new AuditRecord
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "ImportDryRun",
                            EntityId = download.Id.ToString(),
                            EntityType = nameof(Download),
                            Detail = $"Dry run: {verb} {planned.Source} -> {planned.Destination}",
                            Timestamp = now,
                            UserId = userId,
                            CorrelationId = correlationId
                        },
                        ct).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteActivityAuditAsync(
        string operation,
        string entityType,
        string entityId,
        string detail,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        await auditRecordRepository.CreateAsync(
            new AuditRecord
            {
                Category = AuditCategories.Jobs,
                Operation = operation,
                EntityType = entityType,
                EntityId = entityId,
                Detail = detail,
                Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                UserId = userId,
                CorrelationId = correlationId
            },
            cancellationToken).ConfigureAwait(false);
    }

    private ContentTypeDefinition? ResolveContentTypeDefinition(string? contentTypeSlug) =>
        string.IsNullOrWhiteSpace(contentTypeSlug) ? null : contentTypeRegistry.GetDefinitionFor(contentTypeSlug);

    private async Task<string?> MatchBlockedFileTypeAsync(
        string filePath,
        HashSet<string> blockedExtensions,
        HashSet<string> blockedMimes,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(extension) && blockedExtensions.Contains(extension))
        {
            return extension.ToLowerInvariant();
        }

        if (blockedMimes.Count > 0)
        {
            var mime = await fileSystem.LookupMimeTypeAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (mime is not null && blockedMimes.Contains(mime))
            {
                return mime;
            }
        }

        return null;
    }

    private async Task<IReadOnlyDictionary<string, MediaFileProbe>> ProbeFilesAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken)
    {
        using var activity = DownloadTelemetry.Source.StartActivity("ProbeFiles");
        activity?.SetTag("magpie.import.probed_file_count", filePaths.Count);

        var probes = new Dictionary<string, MediaFileProbe>(PathComparer);
        foreach (var filePath in filePaths)
        {
            var mimeType = await fileSystem.LookupMimeTypeAsync(filePath, cancellationToken)
                .ConfigureAwait(false);
            probes[filePath] = new MediaFileProbe(
                filePath,
                fileSystem.ResolveFileKind(mimeType),
                mimeType,
                ReadFileSize(filePath));
        }

        return probes;
    }

    private async Task<FileKind> ResolveFileKindAsync(string filePath, CancellationToken cancellationToken)
    {
        var mimeType = await fileSystem.LookupMimeTypeAsync(filePath, cancellationToken)
            .ConfigureAwait(false);
        return fileSystem.ResolveFileKind(mimeType);
    }

    private async Task SetDownloadScopeAsync(
        Guid downloadId,
        IReadOnlyCollection<Guid> contentHierarchyItemIds,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var existing = await downloadScopeRepository.QueryAllAsync(
            new DownloadScopeQuery { DownloadId = downloadId },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var targetIds = contentHierarchyItemIds.ToHashSet();
        var removedIds = existing
            .Where(scope => !targetIds.Contains(scope.ContentHierarchyItemId))
            .Select(scope => scope.ContentHierarchyItemId)
            .ToList();

        foreach (var scope in existing.Where(scope => !targetIds.Contains(scope.ContentHierarchyItemId)))
        {
            await downloadScopeRepository.DeleteAsync(scope.Id, userId, cancellationToken).ConfigureAwait(false);
        }

        var existingIds = existing.Select(scope => scope.ContentHierarchyItemId).ToHashSet();
        foreach (var contentHierarchyItemId in targetIds.Where(id => !existingIds.Contains(id)))
        {
            await downloadScopeRepository.CreateAsync(
                new DownloadScope
                {
                    Id = Guid.NewGuid(),
                    DownloadId = downloadId,
                    ContentHierarchyItemId = contentHierarchyItemId
                },
                userId,
                cancellationToken).ConfigureAwait(false);
        }

        await RemoveUncoveredReleaseScopeAsync(downloadId, removedIds, userId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RemoveUncoveredReleaseScopeAsync(
        Guid downloadId,
        IReadOnlyCollection<Guid> hierarchyItemIds,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (hierarchyItemIds.Count == 0)
        {
            return;
        }

        var download = await downloadRepository.GetByIdAsync(downloadId, cancellationToken).ConfigureAwait(false);
        if (download is not { Status: DownloadStatus.Imported, ReleaseId: { } releaseId })
        {
            return;
        }

        var activeDownloads = (await downloadRepository.QueryAsync(
            new DownloadQuery
            {
                ReleaseId = releaseId,
                Statuses = Enum.GetValues<DownloadStatus>()
                    .Except([DownloadStatus.Imported, DownloadStatus.Failed, DownloadStatus.Cancelled])
                    .ToList()
            },
            new() { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false)).Items;
        if (activeDownloads.Any())
        {
            return;
        }

        var acquisitions = await jobExecutionRepository.QueryAllAsync(
            new JobExecutionQuery
            {
                JobType = MagpieJobs.Types.ReleaseDownloadItem,
                Statuses = [JobExecutionStatus.Queued, JobExecutionStatus.Running]
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (acquisitions.Any(execution =>
            execution.Parameters.TryGetValue(MagpieJobs.Parameters.ReleaseId, out var value) &&
            Guid.TryParse(value, out var queuedReleaseId) && queuedReleaseId == releaseId))
        {
            return;
        }

        var releaseScopes = await releaseScopeRepository.QueryAllAsync(
            new ReleaseScopeQuery { ReleaseId = releaseId },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var releaseScope in releaseScopes.Where(
            scope => hierarchyItemIds.Contains(scope.ContentHierarchyItemId)))
        {
            var otherDownloadScopes = await downloadScopeRepository.QueryAllAsync(
                new DownloadScopeQuery { ContentHierarchyItemId = releaseScope.ContentHierarchyItemId },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var covered = false;
            foreach (var otherScope in otherDownloadScopes.Where(scope => scope.DownloadId != downloadId))
            {
                var otherDownload = await downloadRepository.GetByIdAsync(otherScope.DownloadId, cancellationToken)
                    .ConfigureAwait(false);
                if (otherDownload?.ReleaseId == releaseId)
                {
                    covered = true;
                    break;
                }
            }

            if (!covered)
            {
                await releaseScopeRepository.DeleteAsync(releaseScope.Id, userId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private long ReadFileSize(string filePath)
    {
        try
        {
            return fileSystem.GetFileSize(filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The job failed to read the size of media file {FilePath}",
                filePath);
            return 0;
        }
    }
}
