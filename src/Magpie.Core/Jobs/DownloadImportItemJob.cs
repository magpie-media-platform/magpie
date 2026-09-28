using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
using Magpie.Sdk.Content;
using Magpie.Sdk.Events;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Media;
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
    IProgressReporter progressReporter,
    IMediaInfoExtractor? mediaInfoExtractor = null) : IJob
{
    internal enum SkipReason
    {
        NotMonitored,
        NoContentItem,
        Acquiring,
        Fulfilled,
        NotAnUpgrade,
        NoSingleItem,
        Extra
    }

    private enum DownloadIdSource
    {
        Parameter,
        EventContext,
    }

    private readonly record struct ImportFileOperation(
        string Source,
        string Destination,
        FileKind FileKind,
        bool IsMainContent,
        Guid ContentHierarchyItemId,
        MediaInfo? MediaInfo = null);

    private readonly record struct MediaFileProbe(
        string FilePath,
        FileKind FileKind,
        string? MimeType,
        long FileSize,
        DateTime? LastWriteTimeUtc);

    private readonly record struct TransferredOperation(ImportFileOperation Operation, ImportMode ExecutedMode);

    private readonly record struct ItemImportGroup(
        ContentItem ContentItem,
        IReadOnlyList<TransferredOperation> Files);

    private readonly record struct ReplaceSnapshot(MediaFile? ExistingMediaFile, long PriorSize);

    private readonly record struct ReplaceBackup(string OriginalPath, string BackupPath, long OriginalSize,
        string? Identity = null, string? Version = null);

    private readonly record struct RecoveryFile(string Source, string Destination, ImportMode Mode, long Size,
        string? SourceIdentity = null, string? PrivatePath = null, string? Identity = null,
        string? SourceVersion = null, string? PublishedVersion = null);

    private readonly record struct SupersededFile(
        Guid MediaFileId,
        Guid ContentHierarchyItemId,
        string RootPath,
        string FilePath,
        long FileSize,
        string? Version);

    private sealed record CapacityRecovery
    {
        public List<RecoveryFile> Files { get; set; } = [];
        public List<ReplaceBackup> Backups { get; set; } = [];
        public List<ImportedContent> Imports { get; set; } = [];
        public List<SupersededFile> Superseded { get; set; } = [];
        public string? Reason { get; set; }
    }

    private sealed record ImportedContent(ContentEventImpact Impact, int FileCount, Guid? UserId,
        Guid? CorrelationId);

    private sealed class CapacityWaitingException(string message) : IOException(message);

    private sealed class ImportSizeUnavailableException() : IOException("The import file size is unavailable.");

    private readonly record struct TransferResult(
        IReadOnlyList<TransferredOperation> Transferred,
        IReadOnlyList<ReplaceBackup> ReplaceBackups,
        IReadOnlyList<RecoveryFile> RecoveryFiles);

    private readonly record struct RollbackResult(
        int TransferCount,
        int RevertedTransferCount,
        int ReplaceBackupCount,
        int RestoredReplaceBackupCount,
        IReadOnlyList<TransferredOperation> RemainingTransfers,
        IReadOnlyList<ReplaceBackup> RemainingBackups)
    {
        public bool HasWork => TransferCount > 0 || ReplaceBackupCount > 0;

        public bool IsComplete =>
            RevertedTransferCount == TransferCount && RestoredReplaceBackupCount == ReplaceBackupCount;

        public string Outcome => IsComplete ? "reverted" : "partially-reverted";
    }

    private readonly record struct ResolvedItemImport(
        ContentItem ContentItem,
        IReadOnlyList<TransferredOperation> Files,
        FulfillmentStatus Prior,
        QualityStatus? QualityStatus);

    private sealed record ImportOperation
    {
        public required IReadOnlyList<ImportFileOperation> PlannedOperations { get; init; }

        public required IReadOnlyList<ImportFileOperation> FilteredOperations { get; init; }

        public required IReadOnlyList<string> ReplaceTargets { get; init; }

        public IReadOnlyList<string> SkippedFiles { get; init; } = [];

        public IDisposable? PathLock { get; init; }
    }

    private sealed class DownloadFileSafetyException(string message, Exception? innerException = null)
        : InvalidOperationException(message, innerException);

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

    private static readonly string[] SubtitleFlags = ["forced", "sdh", "cc"];

    private static readonly HashSet<string> ExtraDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Behind The Scenes",
        "Bonus Features",
        "Deleted Scenes",
        "Extras",
        "Featurettes",
        "Interviews",
        "Other",
        "Sample",
        "Samples",
        "Scenes",
        "Shorts",
        "Special Features",
        "Trailers"
    };

    private static readonly Regex ExtraFileNamePattern = new(
        "^trailer$|-(behindthescenes|deleted|featurette|interview|other|scene|short|trailer)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTypeAndTarget,
        LockPartitionFields = [MagpieJobs.Parameters.DownloadId],
        GuidLockPartitionFields = [MagpieJobs.Parameters.DownloadId],
        LockPartitionEventContextKeys = new Dictionary<string, string>
        {
            [MagpieJobs.Parameters.DownloadId] = MagpieEvents.Keys.DownloadId
        },
        Parameters =
        [
            new(
                MagpieJobs.Parameters.DownloadId,
                "The download id to import.",
                DataType.String,
                true,
                null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            },
            new(
                MagpieJobs.Parameters.DryRun,
                "When true, plan the import without moving files or writing the DB.",
                DataType.Boolean,
                false,
                "false"),
            new(
                MagpieJobs.Parameters.CollisionMode,
                "How to treat destination collisions: 'fail', 'skip', or 'replace'. " +
                "When omitted, new imports inherit the root-folder policy. " +
                "Recovery retains the import's explicit policy.",
                DataType.String,
                false,
                null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) ||
                    (Enum.TryParse<ImportCollisionMode>(value, ignoreCase: true, out var mode) && Enum.IsDefined(mode))
                    ? null : "must be fail, skip, or replace."
            }
        ],
    };

    private const string TorrentProtocol = "torrent";

    private const int MaxStatusReasonLength = 1024;

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

    private static bool IsCapacityFailure(Exception exception)
    {
        if (exception is not IOException)
        {
            return false;
        }

        return exception.HResult is unchecked((int)0x80070070) or unchecked((int)0x80070027) ||
            !OperatingSystem.IsWindows() && (exception.HResult == 28 ||
                OperatingSystem.IsLinux() && exception.HResult == 122 ||
                OperatingSystem.IsMacOS() && exception.HResult == 69);
    }

    private static bool HasSampleDirectorySegment(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        return !string.IsNullOrEmpty(directory) && directory
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Contains("sample", StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsExtra(string releasePath, string filePath) =>
        (Path.GetDirectoryName(Path.GetRelativePath(releasePath, filePath)) ?? string.Empty)
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(ExtraDirectoryNames.Contains) ||
        ReleaseFiles.IsSample(filePath) ||
        ExtraFileNamePattern.IsMatch(Path.GetFileNameWithoutExtension(filePath));

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

    private static void RecordRollbackOutcome(Activity? activity, RollbackResult rollback)
    {
        activity?.SetTag("magpie.import.rollback_outcome", rollback.Outcome);
        activity?.SetTag("magpie.import.rollback_transfer_count", rollback.TransferCount);
        activity?.SetTag("magpie.import.rollback_reverted_transfer_count", rollback.RevertedTransferCount);
        activity?.SetTag("magpie.import.rollback_replace_backup_count", rollback.ReplaceBackupCount);
        activity?.SetTag(
            "magpie.import.rollback_restored_replace_backup_count",
            rollback.RestoredReplaceBackupCount);
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

    internal static string? FindPrimary(string sidecarPath, IReadOnlyList<string> primaryPaths)
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

        var flagSeparator = withoutExtension.LastIndexOf('.');
        if (flagSeparator > 0 &&
            SubtitleFlags.Contains(withoutExtension[(flagSeparator + 1)..], StringComparer.OrdinalIgnoreCase))
        {
            withoutExtension = withoutExtension[..flagSeparator];
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

    private static bool AcceptsPrimaryFiles(ContentTypeDefinition? definition, string level) =>
        definition?.LevelDefinitions.FirstOrDefault(value => value.Slug == level)?.AcceptedFileTypes.Count > 0;

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
            if (!dryRun && download.ImportRecoveryState is { } storedRecovery)
            {
                var recovery = JsonSerializer.Deserialize<CapacityRecovery>(storedRecovery) ??
                    throw new DownloadFileSafetyException("The import recovery state is empty.");
                using var paths = AcquireImportPaths(recovery.Files, recovery.Backups);
                await CompleteCommittedImportAsync(download, recovery, userId, cancellationToken)
                    .ConfigureAwait(false);
            }

            progressReporter.Report(1, "The import completed.");
            return;
        }

        if (download.Status == DownloadStatus.Cancelled)
        {
            progressReporter.Report(1, "The download was cancelled. No files were imported.");
            return;
        }

        if (download.Status is not (DownloadStatus.Downloaded or DownloadStatus.Importing))
        {
            throw new InvalidOperationException(
                $"Download {downloadId} is in status {download.Status} and cannot be imported");
        }

        if (!dryRun && download.Status == DownloadStatus.Downloaded)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var started = collisionMode is null
                ? await downloadRepository.MarkImportingAsync(downloadId, now, userId, cancellationToken)
                    .ConfigureAwait(false)
                : await downloadRepository.MarkImportingAsync(downloadId, now, userId, collisionMode, cancellationToken)
                    .ConfigureAwait(false);
            if (!started)
            {
                progressReporter.Report(1, "The download status changed before import could start.");
                return;
            }
        }

        if (download.Status == DownloadStatus.Importing)
        {
            if (!dryRun && collisionMode is { } explicitMode &&
                !await downloadRepository.SetImportCollisionModeAsync(downloadId, explicitMode, cancellationToken)
                    .ConfigureAwait(false))
            {
                progressReporter.Report(1, "The download status changed before import could resume.");
                return;
            }

            collisionMode ??= download.ImportCollisionMode;
        }

        var dryRunTag = new KeyValuePair<string, object?>("dry_run", dryRun);
        try
        {
            var imported = await ImportCoreAsync(
                download,
                userId,
                correlationId,
                dryRun,
                collisionMode,
                progressReporter,
                activity,
                cancellationToken)
                .ConfigureAwait(false);
            DownloadTelemetry.Imports.Add(1, new("outcome", imported ? "success" : "waiting"), dryRunTag);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CapacityWaitingException ex)
        {
            progressReporter.Report(1, ex.Message);
            DownloadTelemetry.Imports.Add(1, new("outcome", "waiting"), dryRunTag);
        }
        catch (DownloadFileSafetyException ex)
        {
            var current = await downloadRepository.GetByIdAsync(downloadId, CancellationToken.None)
                .ConfigureAwait(false);
            if (current?.Status == DownloadStatus.Importing)
            {
                var reason = current.ImportRecoveryState is null ? ex.Message :
                    ex.Message + " File recovery is incomplete; cleanup is deferred.";
                await downloadRepository.SetImportCapacityReasonAsync(downloadId, reason,
                    timeProvider.GetUtcNow().UtcDateTime, userId, CancellationToken.None).ConfigureAwait(false);
            }

            DownloadTelemetry.Imports.Add(1, new("outcome", "failure"), dryRunTag);
            throw;
        }
        catch (Exception ex) when (!dryRun)
        {
            var current = await downloadRepository.GetByIdAsync(downloadId, CancellationToken.None)
                .ConfigureAwait(false);
            if (current is null || current.Status == DownloadStatus.Imported || current.ImportRecoveryState is not null)
            {
                throw new DownloadFileSafetyException(
                    "The import failed after file ownership changed. Its files and current status are retained.", ex);
            }

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("error.type", ex.GetType().FullName);
            activity?.SetTag("magpie.download.failure_reason", nameof(DownloadFailureReason.ImportFailed));
            DownloadTelemetry.Imports.Add(1, new("outcome", "failure"), dryRunTag);
            await MarkImportFailedAsync(download, userId, correlationId, ex, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task<bool> ImportCoreAsync(
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
        if (!dryRun && download.ImportRecoveryState is { } storedRecovery)
        {
            CapacityRecovery recovery;
            try
            {
                recovery = JsonSerializer.Deserialize<CapacityRecovery>(storedRecovery) ??
                    throw new InvalidOperationException("The import recovery state is empty.");
            }
            catch (Exception ex)
            {
                throw new DownloadFileSafetyException("The import recovery state could not be read. " + ex.Message);
            }

            recovery.Reason ??= "The import was interrupted.";
            using var paths = AcquireImportPaths(recovery.Files, recovery.Backups);
            await StoreCapacityRecoveryAsync(downloadId, recovery, userId, cancellationToken).ConfigureAwait(false);
            if (!await RecoverCapacityTransferAsync(downloadId, recovery, userId, cancellationToken)
                .ConfigureAwait(false))
            {
                progress.Report(1, recovery.Reason + " File recovery is incomplete; the original files are retained.");
                return false;
            }
        }

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

        var acquisitionScopes = await downloadScopeRepository.QueryAllAsync(
            new DownloadScopeQuery { DownloadId = downloadId },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var acquisitionScopeNodeIds = acquisitionScopes
            .Select(scope => scope.ContentHierarchyItemId)
            .Distinct()
            .ToList();
        var acquisitionLeaves = await ResolveScopedLeavesAsync(acquisitionScopeNodeIds, cancellationToken)
            .ConfigureAwait(false);

        var operation = await BuildImportOperationAsync(
            download,
            hierarchyItem,
            effectiveCollisionMode,
            transferMode,
            dryRun,
            userId,
            correlationId,
            progress,
            importActivity,
            release,
            acquisitionScopeNodeIds,
            rootFolder.UpgradeFileHandling,
            cancellationToken)
            .ConfigureAwait(false);

        using var operationPaths = operation.PathLock;
        if (dryRun)
        {
            await WriteDryRunPlanAsync(
                download,
                operation.PlannedOperations,
                operation.SkippedFiles,
                transferMode,
                userId,
                correlationId,
                cancellationToken)
                .ConfigureAwait(false);
            progress.Report(
                1,
                $"Dry run: {TransferModeName(transferMode)} {operation.PlannedOperations.Count} file(s). " +
                "The audit history of the download holds the plan.");
            return true;
        }

        var capacityReason = CheckDestinationCapacity(operation.FilteredOperations, transferMode);
        await downloadRepository.SetImportCapacityReasonAsync(downloadId, capacityReason,
            timeProvider.GetUtcNow().UtcDateTime, userId, cancellationToken).ConfigureAwait(false);
        if (capacityReason is not null)
        {
            progress.Report(1, capacityReason);
            return false;
        }

        var replaceSnapshots = await CaptureReplaceSnapshotsAsync(operation.ReplaceTargets, cancellationToken)
            .ConfigureAwait(false);

        var transferResult = await TransferAsync(
            transferMode,
            operation.FilteredOperations,
            operation.ReplaceTargets,
            downloadId,
            userId,
            progress,
            cancellationToken).ConfigureAwait(false);
        var transferred = transferResult.Transferred;
        var grabTargetNode = ancestors[0];
        var persisted = false;
        var committed = new CapacityRecovery
        {
            Files = [.. transferResult.RecoveryFiles],
            Backups = [.. transferResult.ReplaceBackups]
        };
        IReadOnlyList<ItemImportGroup> groups = [];
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(null, "The import will save the media files.");

            groups = await GroupByBoundItemAsync(contentItem, transferred, userId, cancellationToken)
                .ConfigureAwait(false);

            var coveredNodeIds = new HashSet<Guid>();
            foreach (var group in groups.Where(group => group.Files.Any(file => file.Operation.IsMainContent)))
            {
                coveredNodeIds.UnionWith((await contentHierarchyItemRepository
                    .GetLineageAsync(group.ContentItem.ContentHierarchyItemId, cancellationToken)
                    .ConfigureAwait(false)).Select(node => node.Id));
            }

            var uncoveredLeafIds = acquisitionLeaves
                .Select(leaf => leaf.Id)
                .Where(leafId => !coveredNodeIds.Contains(leafId))
                .ToHashSet();
            var reconciledDownloadScopeNodeIds = acquisitionScopeNodeIds
                .Where(nodeId => !uncoveredLeafIds.Contains(nodeId))
                .ToList();
            if (transferResult.RecoveryFiles.Any(file => file.Identity is null ||
                    fileSystem.GetFileIdentity(file.Destination) != file.Identity || file.PublishedVersion is null ||
                    fileSystem.GetFileVersion(file.Destination) != file.PublishedVersion ||
                    fileSystem.GetFileSize(file.Destination) != file.Size))
            {
                throw new DownloadFileSafetyException("The destination changed before import persistence.");
            }

            await PersistAndPublishAsync(
                download,
                contentItem,
                release,
                groups,
                reconciledDownloadScopeNodeIds,
                replaceSnapshots,
                rootFolder,
                operation.SkippedFiles,
                committed,
                userId,
                correlationId,
                () => persisted = true,
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (!persisted)
            {
                try
                {
                    var current = await downloadRepository.GetByIdAsync(downloadId, CancellationToken.None)
                        .ConfigureAwait(false);
                    persisted = current?.Status == DownloadStatus.Imported;
                    if (current is null)
                    {
                        throw new InvalidOperationException("The download could not be found after persistence.");
                    }
                }
                catch (Exception readException)
                {
                    throw new DownloadFileSafetyException("The import commit outcome could not be read. " +
                        "The files and recovery state are retained.", readException);
                }
            }

            if (!persisted)
            {
                var recovery = new CapacityRecovery
                {
                    Files = [.. transferResult.RecoveryFiles],
                    Backups = [.. transferResult.ReplaceBackups],
                    Reason = "The import could not be committed. File recovery will retry."
                };
                await StoreCapacityRecoveryAsync(downloadId, recovery, userId, CancellationToken.None)
                    .ConfigureAwait(false);
                var complete = await RecoverCapacityTransferAsync(downloadId, recovery, userId, CancellationToken.None)
                    .ConfigureAwait(false);
                var rollback = new RollbackResult(transferred.Count, transferred.Count - recovery.Files.Count,
                    transferResult.ReplaceBackups.Count, transferResult.ReplaceBackups.Count - recovery.Backups.Count,
                    [], recovery.Backups);
                RecordRollbackOutcome(importActivity, rollback);
                if (complete)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }

                cancellationToken.ThrowIfCancellationRequested();
                throw new DownloadFileSafetyException("The import could not be committed. File recovery is " +
                    "incomplete; the download and recovery state are retained.", ex);
            }

            logger.LogWarning(ex, "The import of download {DownloadId} committed before an error occurred.",
                downloadId);
        }

        await transactionManager.OnCommittedAsync(
            token => CompleteCommittedImportAsync(download, committed, userId, token),
            cancellationToken).ConfigureAwait(false);
        progress.Report(1, "The import completed.");
        return true;
    }

    private string? CheckDestinationCapacity(IReadOnlyList<ImportFileOperation> operations, ImportMode mode)
    {
        var volumes = new Dictionary<string, (long Required, long Available)>(StringComparer.Ordinal);
        var totalRequired = 0L;
        var minimumAvailable = long.MaxValue;
        var hasUnknownVolume = false;
        foreach (var operation in operations)
        {
            var destination = RootFolderService.ReadDestinationCapacity(fileSystem, operation.Destination);
            long? required = null;
            try
            {
                if (mode == ImportMode.Move)
                {
                    var source = RootFolderService.ReadDestinationCapacity(fileSystem, operation.Source);
                    if (string.IsNullOrWhiteSpace(source?.VolumeId) ||
                        string.IsNullOrWhiteSpace(destination?.VolumeId))
                    {
                        return RootFolderService.CapacityWaitingReason(null, destination?.AvailableBytes);
                    }

                    required = source.VolumeId == destination.VolumeId ? 0 : fileSystem.GetFileSize(operation.Source);
                }
                else
                {
                    required = fileSystem.GetFileSize(operation.Source);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "The import could not read the required capacity for {DownloadPath}.",
                    operation.Source);
            }

            if (required is null or < 0 || destination is null)
            {
                return RootFolderService.CapacityWaitingReason(required, destination?.AvailableBytes);
            }

            if (required > long.MaxValue - totalRequired)
            {
                return RootFolderService.CapacityWaitingReason(null, destination.AvailableBytes);
            }

            totalRequired += required.Value;
            minimumAvailable = Math.Min(minimumAvailable, destination.AvailableBytes);
            if (string.IsNullOrWhiteSpace(destination.VolumeId))
            {
                hasUnknownVolume = true;
                continue;
            }

            var prior = volumes.GetValueOrDefault(destination.VolumeId,
                (Required: 0L, Available: destination.AvailableBytes));
            if (required > long.MaxValue - prior.Required)
            {
                return RootFolderService.CapacityWaitingReason(null, destination.AvailableBytes);
            }

            volumes[destination.VolumeId] = (prior.Required + required.Value,
                Math.Min(prior.Available, destination.AvailableBytes));
        }

        // Without identities, use an upper bound for each destination; never assume separate allocation pools.
        if (hasUnknownVolume)
        {
            return RootFolderService.CapacityWaitingReason(totalRequired, minimumAvailable);
        }

        return volumes.Values.Select(volume =>
                RootFolderService.CapacityWaitingReason(volume.Required, volume.Available))
            .FirstOrDefault(reason => reason is not null);
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

    internal static string SummarizeSkippedFiles(
        string prefix,
        IReadOnlyList<(string File, string? ItemName, SkipReason Reason)> skipped)
    {
        var counts = string.Join(
            ", ",
            skipped
                .GroupBy(file => Describe(file.Reason), StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Count()} {group.Key}"));
        var summary = new StringBuilder(prefix)
            .Append(CultureInfo.InvariantCulture, $"Skipped {skipped.Count} file{(skipped.Count == 1 ? "" : "s")}: ")
            .Append(counts)
            .Append('.');
        for (var index = 0; index < skipped.Count; index++)
        {
            var entry = (index == 0 ? " " : "; ") + FormatSkippedFile(skipped[index]);
            var laterCount = skipped.Count - index - 1;
            var laterSuffix = laterCount > 0 ? $" …and {laterCount} more" : string.Empty;
            if (summary.Length + entry.Length + laterSuffix.Length > MaxStatusReasonLength)
            {
                return summary.Append(CultureInfo.InvariantCulture, $" …and {skipped.Count - index} more").ToString();
            }

            summary.Append(entry);
        }

        return summary.ToString();
    }

    private static string FormatSkippedFile((string File, string? ItemName, SkipReason Reason) skipped) =>
        skipped.ItemName is null
            ? $"{skipped.File} ({Describe(skipped.Reason)})"
            : $"{skipped.File} ({skipped.ItemName}: {Describe(skipped.Reason)})";

    private static string Describe(SkipReason reason) =>
        reason switch
        {
            SkipReason.NotMonitored => "not monitored",
            SkipReason.NoContentItem => "has no content item",
            SkipReason.Acquiring => "another download is acquiring it",
            SkipReason.Fulfilled => "already fulfilled",
            SkipReason.NotAnUpgrade => "not an upgrade over the current file",
            SkipReason.NoSingleItem => "matches no single item",
            SkipReason.Extra => "extra",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };

    private async Task<(
            Dictionary<string, Guid> Bound,
            List<(string File, string? ItemName, SkipReason Reason)> Skipped)>
        BindPrimaryFilesAsync(
        Release release,
        ContentHierarchyItem importTargetNode,
        ContentTypeDefinition? definition,
        IReadOnlyCollection<Guid> acquisitionScopeNodeIds,
        string releasePath,
        IReadOnlyList<string> primaryFiles,
        CancellationToken cancellationToken)
    {
        var bound = new Dictionary<string, Guid>(PathComparer);
        var skipped = new List<(string File, string? ItemName, SkipReason Reason)>();
        if (definition?.LevelDefinitions.Any(level => level.ParentSlug == importTargetNode.Level) != true)
        {
            foreach (var file in primaryFiles)
            {
                if (IsExtra(releasePath, file))
                {
                    skipped.Add((Path.GetFileName(file), null, SkipReason.Extra));
                }
                else
                {
                    bound[file] = importTargetNode.Id;
                }
            }

            return (bound, skipped);
        }

        var subtree = (await contentHierarchyItemRepository.QueryAllAsync(
                new() { AncestorId = importTargetNode.Id },
                cancellationToken: cancellationToken).ConfigureAwait(false))
            .Where(item => item.Id != importTargetNode.Id)
            .ToList();
        var parentById = subtree.ToDictionary(item => item.Id, item => item.ParentId);
        var parentIds = subtree.Where(item => item.ParentId is not null)
            .Select(item => item.ParentId!.Value)
            .ToHashSet();
        var leaves = subtree.Where(item => !parentIds.Contains(item.Id) && AcceptsPrimaryFiles(definition, item.Level))
            .ToList();
        var scopeIds = acquisitionScopeNodeIds.ToHashSet();
        var unbound = new List<string>();
        if (leaves.Count == 0)
        {
            unbound.AddRange(primaryFiles);
        }
        else
        {
            var assignments = await releaseEvaluationService.AssignFilesToLeavesAsync(
                release,
                importTargetNode,
                leaves,
                [.. primaryFiles.Select(file => new ReleaseFileRef(file, Path.GetFileName(file)))],
                cancellationToken).ConfigureAwait(false);
            var skipReasons = new Dictionary<Guid, SkipReason?>();
            foreach (var assignment in assignments)
            {
                var leaf = leaves.FirstOrDefault(value => value.Id == assignment.LeafContentHierarchyItemId);
                if (leaf is null)
                {
                    unbound.Add(assignment.FileKey);
                    continue;
                }

                if (!skipReasons.TryGetValue(leaf.Id, out var reason))
                {
                    var scopedAncestor = false;
                    for (var nodeId = leaf.ParentId; nodeId is { } id && !scopedAncestor;
                        nodeId = parentById.GetValueOrDefault(id))
                    {
                        scopedAncestor = scopeIds.Contains(id);
                    }

                    if (scopeIds.Contains(leaf.Id))
                    {
                        reason = null;
                    }
                    else if (scopedAncestor)
                    {
                        var contentItem = await FindContentItemAsync(leaf, cancellationToken).ConfigureAwait(false);
                        reason = contentItem is null ||
                            contentItem is { ActiveAcquisitionStatus: null, Fulfillment: FulfillmentStatus.Missing }
                            ? null
                            : await ResolveSkipReasonAsync(release, leaf, contentItem, cancellationToken)
                                .ConfigureAwait(false);
                    }
                    else if (leaf.EffectiveMonitoring == MonitoringMode.Unmonitored)
                    {
                        reason = SkipReason.NotMonitored;
                    }
                    else
                    {
                        var contentItem = await FindContentItemAsync(leaf, cancellationToken).ConfigureAwait(false);
                        reason = contentItem is null
                            ? SkipReason.NoContentItem
                            : await ResolveSkipReasonAsync(release, leaf, contentItem, cancellationToken)
                                .ConfigureAwait(false);
                    }

                    skipReasons[leaf.Id] = reason;
                }

                if (reason is { } skipReason)
                {
                    skipped.Add((Path.GetFileName(assignment.FileKey), leaf.Name, skipReason));
                }
                else
                {
                    bound[assignment.FileKey] = leaf.Id;
                }
            }
        }

        var targetFileTypes = definition.LevelDefinitions
            .FirstOrDefault(level => level.Slug == importTargetNode.Level)?.AcceptedFileTypes ?? [];
        foreach (var file in unbound)
        {
            if (targetFileTypes.Count > 0 &&
                await fileSystem.MatchesAnyFileTypeAsync(file, targetFileTypes, cancellationToken)
                    .ConfigureAwait(false))
            {
                bound[file] = importTargetNode.Id;
            }
            else
            {
                skipped.Add((Path.GetFileName(file), null, SkipReason.NoSingleItem));
            }
        }

        return (bound, skipped);
    }

    private async Task<ContentItem?> FindContentItemAsync(
        ContentHierarchyItem leaf,
        CancellationToken cancellationToken) =>
        leaf.ContentItem ?? (await contentRepository.QueryAsync(
            new ContentItemQuery { ContentHierarchyItemId = leaf.Id },
            new() { PageNumber = 1, PageSize = 2 },
            null,
            cancellationToken).ConfigureAwait(false)).Items.SingleOrDefault();

    private async Task<SkipReason?> ResolveSkipReasonAsync(
        Release release,
        ContentHierarchyItem leaf,
        ContentItem contentItem,
        CancellationToken cancellationToken)
    {
        if (contentItem.ActiveAcquisitionStatus is not null)
        {
            return SkipReason.Acquiring;
        }

        if (contentItem.Fulfillment is not FulfillmentStatus.Missing and not FulfillmentStatus.Upgradable)
        {
            return SkipReason.Fulfilled;
        }

        var assessment = await releaseEvaluationService.AssessForContentAsync(release, [], leaf.Id, cancellationToken)
            .ConfigureAwait(false);
        return assessment.IsAcceptable is true ? null : SkipReason.NotAnUpgrade;
    }

    private async Task<bool> IsSameItemUpgradeAsync(
        Release release,
        ImportFileOperation operation,
        CancellationToken cancellationToken)
    {
        var existing = await mediaFileRepository.GetByPathAsync(operation.Destination, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        var owner = (await contentRepository.QueryAsync(
            new ContentItemQuery { ContentHierarchyItemId = operation.ContentHierarchyItemId },
            new() { PageNumber = 1, PageSize = 2 },
            null,
            cancellationToken).ConfigureAwait(false)).Items.SingleOrDefault();
        return owner?.Id == existing.ContentItemId &&
            (await releaseEvaluationService.AssessForContentAsync(
                release, [], operation.ContentHierarchyItemId, cancellationToken).ConfigureAwait(false))
            .IsAcceptable is true;
    }

    private async Task<ImportOperation> BuildImportOperationAsync(
        Download download,
        ContentHierarchyItem contentHierarchyItem,
        ImportCollisionMode effectiveCollisionMode,
        ImportMode transferMode,
        bool dryRun,
        Guid? userId,
        Guid? correlationId,
        IProgressReporter progress,
        Activity? importActivity,
        Release release,
        IReadOnlyCollection<Guid> acquisitionScopeNodeIds,
        UpgradeFileHandling upgradeFileHandling,
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

        var (bindings, skipped) = await BindPrimaryFilesAsync(
            release,
            contentHierarchyItem,
            contentTypeDefinition,
            acquisitionScopeNodeIds,
            importSourcePath,
            primaryCandidates,
            cancellationToken).ConfigureAwait(false);
        var skippedFiles = skipped.Select(FormatSkippedFile).ToList();
        if (skippedFiles.Count > 0)
        {
            logger.LogWarning(
                "Download {DownloadId} skips {SkippedCount} of {TotalCount} file(s) before transfer: {SkippedFiles}",
                downloadId,
                skippedFiles.Count,
                primaryCandidates.Count,
                string.Join("; ", skippedFiles));
        }

        if (bindings.Count == 0)
        {
            SetFailureReason(activity, importActivity, DownloadFailureReason.NoUsableContent);
            var skippedSummary = SummarizeSkippedFiles("No file matched content that needs it. ", skipped);
            if (!dryRun)
            {
                await MarkDownloadFailedAsync(
                    download,
                    DownloadFailureReason.NoUsableContent,
                    skippedSummary,
                    userId,
                    correlationId,
                    cancellationToken)
                    .ConfigureAwait(false);
            }

            throw new DownloadFileSafetyException(
                $"Download {downloadId} has no file for content that needs it. {skippedSummary}");
        }

        var acceptedFiles = new List<string>(primaryCandidates.Count + pairedSidecars.Count);
        var filePathComparer = StringComparer.Create(
            CultureInfo.InvariantCulture,
            CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

        acceptedFiles.AddRange(primaryCandidates.Where(bindings.ContainsKey).OrderBy(path => path, filePathComparer));
        acceptedFiles.AddRange(pairedSidecars
            .Where(sidecar => FindPrimary(sidecar, primaryCandidates) is { } primary && bindings.ContainsKey(primary))
            .OrderBy(path => path, filePathComparer));

        var primaryPaths = primaryCandidates.ToHashSet(PathComparer);
        var plannedOps = new List<ImportFileOperation>(acceptedFiles.Count);

        var probedMediaInfo = new Dictionary<string, MediaInfo?>(PathComparer);
        foreach (var primary in primaryCandidates)
        {
            probedMediaInfo[primary] = await ProbeMediaInfoAsync(primary, cancellationToken).ConfigureAwait(false);
        }

        var primaryDestinations = new Dictionary<string, string>(PathComparer);
        foreach (var source in acceptedFiles)
        {
            var isPrimary = primaryPaths.Contains(source);
            var primary = isPrimary ? source : FindPrimary(source, primaryCandidates)!;
            var mediaInfo = probedMediaInfo.GetValueOrDefault(primary);
            var boundItemId = bindings[primary];
            string destination;
            if (isPrimary)
            {
                var preview = await contentFileService
                    .PreviewRenameAsync(boundItemId, new(source), mediaInfo, cancellationToken)
                    .ConfigureAwait(false);
                destination = preview.Rename.FullName;
                primaryDestinations[source] = destination;
            }
            else
            {
                var primaryDestination = primaryDestinations[primary];
                var suffix = Path.GetFileName(source)[Path.GetFileNameWithoutExtension(primary).Length..];
                destination = Path.Combine(
                    Path.GetDirectoryName(primaryDestination)!,
                    Path.GetFileNameWithoutExtension(primaryDestination) + suffix);
            }

            plannedOps.Add(new ImportFileOperation(
                source,
                destination,
                await ResolveFileKindAsync(source, cancellationToken).ConfigureAwait(false),
                isPrimary,
                boundItemId,
                isPrimary ? mediaInfo : null));
        }

        plannedOps = RemoveDuplicatePlannedOperations(plannedOps, downloadId);
        if (dryRun)
        {
            return new ImportOperation
            {
                PlannedOperations = plannedOps,
                FilteredOperations = [],
                ReplaceTargets = [],
                SkippedFiles = skippedFiles
            };
        }

        var pathLock = AcquireImportPaths(plannedOps.Select(op =>
            new RecoveryFile(op.Source, op.Destination, transferMode, 0)).ToList(), []);
        try
        {
            var replaceTargets = new List<string>();
            var filteredOps = new List<ImportFileOperation>(plannedOps.Count);
            foreach (var op in plannedOps)
            {
                if (!fileSystem.FileExists(op.Destination))
                {
                    filteredOps.Add(op);
                    continue;
                }

                if (upgradeFileHandling == UpgradeFileHandling.Replace &&
                    await IsSameItemUpgradeAsync(release, op, cancellationToken).ConfigureAwait(false))
                {
                    replaceTargets.Add(op.Destination);
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
                    "Failed/NoUsableContent",
                    downloadId);
                SetFailureReason(activity, importActivity, DownloadFailureReason.NoUsableContent);
                await MarkDownloadFailedAsync(
                    download,
                    DownloadFailureReason.NoUsableContent,
                    "Every planned file exists at its destination. The job imported no files.",
                    userId,
                    correlationId,
                    cancellationToken)
                    .ConfigureAwait(false);
                throw new DownloadFileSafetyException(
                    $"Download {downloadId} imported no files: every planned operation collided under " +
                    "Skip collision mode. The download has been failed with reason NoUsableContent.");
            }

            return new ImportOperation
            {
                PlannedOperations = plannedOps,
                FilteredOperations = filteredOps,
                ReplaceTargets = replaceTargets,
                SkippedFiles = skippedFiles,
                PathLock = pathLock
            };
        }
        catch
        {
            pathLock?.Dispose();
            throw;
        }
    }

    private async Task<MediaInfo?> ProbeMediaInfoAsync(string path, CancellationToken cancellationToken)
    {
        if (mediaInfoExtractor is null)
        {
            return null;
        }

        try
        {
            return await mediaInfoExtractor.ExtractAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The job could not get media info for {FilePath} before placement and imports it without media info",
                path);
            return null;
        }
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

    private async Task<TransferResult> TransferAsync(
        ImportMode transferMode,
        IReadOnlyList<ImportFileOperation> plannedOps,
        IReadOnlyList<string> replaceTargets,
        Guid downloadId,
        Guid? userId,
        IProgressReporter progress,
        CancellationToken cancellationToken)
    {
        using var activity = DownloadTelemetry.Source.StartActivity("Transfer");
        activity?.SetTag("magpie.import.file_count", plannedOps.Count);
        activity?.SetTag("magpie.import.transfer_mode", TransferModeName(transferMode));

        var backups = new List<ReplaceBackup>(replaceTargets.Count);
        var transferred = new List<TransferredOperation>(plannedOps.Count);
        var recoveryFiles = new List<RecoveryFile>(plannedOps.Count);
        ReplaceBackup? pendingBackup = null;
        var failingOp = default(ImportFileOperation);
        try
        {
            foreach (var target in replaceTargets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var backup = new ReplaceBackup(
                    target,
                    BuildBackupPath(target, downloadId),
                    fileSystem.GetFileSize(target),
                    RequireFileIdentity(target), RequireFileVersion(target));
                pendingBackup = backup;
                await StoreCapacityRecoveryAsync(downloadId, new CapacityRecovery
                {
                    Backups = [.. backups, backup],
                    Reason = null
                }, userId, cancellationToken).ConfigureAwait(false);
                fileSystem.Move(target, backup.BackupPath);
                backups.Add(backup);
                pendingBackup = null;
            }

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

                var privatePath = Path.Combine(destDir!, ".magpie-import-" + Guid.NewGuid().ToString("N"), "payload");
                var recoveryFile = new RecoveryFile(op.Source, op.Destination, transferMode,
                    RequireImportFileSize(op.Source), RequireFileIdentity(op.Source), privatePath,
                    SourceVersion: RequireFileVersion(op.Source));
                recoveryFiles.Add(recoveryFile);
                await StoreCapacityRecoveryAsync(downloadId, new CapacityRecovery
                {
                    Files = recoveryFiles,
                    Backups = backups,
                    Reason = null
                }, userId, cancellationToken).ConfigureAwait(false);
                fileSystem.CreateDirectory(Path.GetDirectoryName(privatePath)!);
                await fileSystem.TransferToOwnedPathAsync(op.Source, privatePath, transferMode, async identity =>
                {
                    recoveryFile = recoveryFile with { Identity = identity };
                    recoveryFiles[i] = recoveryFile;
                    await StoreCapacityRecoveryAsync(downloadId, new CapacityRecovery
                    {
                        Files = recoveryFiles,
                        Backups = backups
                    }, userId, cancellationToken).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
                if (recoveryFile.Identity is null)
                {
                    throw new DownloadFileSafetyException("The transfer did not establish file ownership.");
                }

                recoveryFile = recoveryFile with { PublishedVersion = RequireFileVersion(privatePath) };
                recoveryFiles[i] = recoveryFile;
                await StoreCapacityRecoveryAsync(downloadId, new CapacityRecovery
                {
                    Files = recoveryFiles,
                    Backups = backups
                }, userId, cancellationToken).ConfigureAwait(false);
                fileSystem.Move(privatePath, op.Destination);
                RemoveStagingDirectory(recoveryFile);
                transferred.Add(new TransferredOperation(op, transferMode));
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            var rollback = await RollbackTransfersAsync(recoveryFiles, transferred,
                pendingBackup is { } cancelledBackup ? [.. backups, cancelledBackup] : backups, downloadId, userId,
                "The import was cancelled.").ConfigureAwait(false);
            RecordRollbackOutcome(activity, rollback);
            RecordTransferOutcome(activity, transferred.Count, rollback.Outcome);

            throw;
        }
        catch (DownloadFileSafetyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (IsCapacityFailure(ex) || ex is FileSystem.StorageCapacityUnavailableException or
                ImportSizeUnavailableException)
            {
                string reason;
                if (ex is FileSystem.StorageCapacityUnavailableException unavailable)
                {
                    reason = RootFolderService.CapacityWaitingReason(
                        transferMode == ImportMode.Copy && recoveryFiles.Count > 0 ? recoveryFiles[^1].Size : null,
                        unavailable.AvailableBytes)!;
                }
                else if (ex is ImportSizeUnavailableException)
                {
                    reason = RootFolderService.CapacityWaitingReason(null,
                        RootFolderService.ReadDestinationCapacity(fileSystem, failingOp.Destination)?.AvailableBytes)!;
                }
                else
                {
                    var capacity = string.IsNullOrEmpty(failingOp.Destination) ? null :
                        RootFolderService.ReadDestinationCapacity(fileSystem, failingOp.Destination);
                    var required = recoveryFiles.Count == 0 ? (long?)null : recoveryFiles[^1].Size;
                    if (transferMode == ImportMode.Move && !string.IsNullOrEmpty(failingOp.Source))
                    {
                        var sourceCapacity = RootFolderService.ReadDestinationCapacity(fileSystem, failingOp.Source);
                        required = string.IsNullOrWhiteSpace(sourceCapacity?.VolumeId) ||
                            string.IsNullOrWhiteSpace(capacity?.VolumeId) ? null :
                            sourceCapacity.VolumeId == capacity.VolumeId ? 0 : required;
                    }

                    reason = "Waiting for destination capacity: the destination reported insufficient space " +
                        "during transfer. Required: " +
                        (required?.ToString(CultureInfo.InvariantCulture) ?? "unknown") +
                        (required is null ? "." : " bytes.") + " Available: " +
                        (capacity?.AvailableBytes.ToString(CultureInfo.InvariantCulture) ?? "unknown") +
                        (capacity is null ? "." : " bytes.");
                }

                var recovery = new CapacityRecovery
                {
                    Files = recoveryFiles,
                    Backups = pendingBackup is { } pending ? [.. backups, pending] : backups,
                    Reason = reason
                };
                await StoreCapacityRecoveryAsync(downloadId, recovery, userId, CancellationToken.None)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var complete = await RecoverCapacityTransferAsync(downloadId, recovery, userId, cancellationToken)
                    .ConfigureAwait(false);
                throw new CapacityWaitingException(complete ? reason :
                    reason + " File recovery is incomplete; the original files are retained.");
            }

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            activity?.SetTag("error.type", ex.GetType().FullName);
            logger.LogError(
                ex,
                "The job failed to {TransferMode} {SourcePath} to {DestinationPath} for download {DownloadId}",
                transferMode,
                failingOp.Source,
                failingOp.Destination,
                downloadId);

            var rollback = await RollbackTransfersAsync(recoveryFiles, transferred,
                pendingBackup is { } failedBackup ? [.. backups, failedBackup] : backups, downloadId, userId,
                "The import transfer failed.").ConfigureAwait(false);
            RecordRollbackOutcome(activity, rollback);
            RecordTransferOutcome(activity, transferred.Count, rollback.HasWork ? rollback.Outcome : "failed");

            throw new InvalidOperationException(
                $"Import of download {downloadId} failed after transferring {transferred.Count} file(s). " +
                $"Rollback outcome: {rollback.Outcome}. " +
                $"Reverted {rollback.RevertedTransferCount} of {rollback.TransferCount} transferred file(s) and " +
                $"restored {rollback.RestoredReplaceBackupCount} of {rollback.ReplaceBackupCount} " +
                "pre-existing file(s). " +
                $"Failing transfer: {failingOp.Source} -> {failingOp.Destination}. {ex.Message}",
                ex);
        }

        RecordTransferOutcome(activity, transferred.Count, "success");
        return new TransferResult(transferred, backups, recoveryFiles);
    }

    private async Task StoreCapacityRecoveryAsync(Guid downloadId, CapacityRecovery recovery, Guid? userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var pending = recovery.Files.Count > 0 || recovery.Backups.Count > 0 || recovery.Imports.Count > 0 ||
                recovery.Superseded.Count > 0;
            await downloadRepository.SetImportRecoveryStateAsync(downloadId,
                pending ? JsonSerializer.Serialize(recovery) : null,
                pending && recovery.Reason is not null ?
                    recovery.Reason + " File recovery is incomplete; the original files are retained." :
                    recovery.Reason,
                timeProvider.GetUtcNow().UtcDateTime, userId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DownloadFileSafetyException("The import could not store file recovery state. " + ex.Message);
        }
    }

    private IDisposable AcquireImportPaths(IReadOnlyList<RecoveryFile> files, IReadOnlyList<ReplaceBackup> backups)
    {
        try
        {
            return fileSystem.AcquireImportPaths(files.SelectMany(file => file.Mode == ImportMode.Move ?
                    new[] { file.Source, file.Destination } : [file.Destination])
                .Concat(backups.Select(backup => backup.OriginalPath)).Distinct(PathComparer).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new DownloadFileSafetyException(IsCapacityFailure(ex) ?
                "Waiting for destination capacity: file coordination reported insufficient space." :
                "Waiting for exclusive access to the import files.", ex);
        }
    }

    private string RequireFileIdentity(string path) => fileSystem.GetFileIdentity(path) ??
        throw new FileSystem.FileOwnershipUnavailableException(
            "The file identity is unavailable, so the import cannot confirm file ownership.");

    private void RemoveStagingDirectory(RecoveryFile file)
    {
        var directory = Path.GetDirectoryName(file.PrivatePath);
        if (Path.GetFileName(file.PrivatePath) != "payload" ||
            Path.GetFileName(directory)?.StartsWith(".magpie-import-", StringComparison.Ordinal) != true)
        {
            return;
        }

        try
        {
            fileSystem.DeleteEmptyDirectory(directory!);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogDebug(
                ex,
                "The import could not remove the staging directory {Directory}.",
                directory);
        }
    }

    private string RequireFileVersion(string path) => fileSystem.GetFileVersion(path) ??
        throw new DownloadFileSafetyException("File version is unavailable. The import will retry.");

    private long RequireImportFileSize(string path)
    {
        try
        {
            var size = fileSystem.GetFileSize(path);
            if (size >= 0)
            {
                return size;
            }
        }
        catch (Exception ex) when (ex is not FileNotFoundException and not DirectoryNotFoundException &&
            ex is IOException or UnauthorizedAccessException or NotSupportedException or
                System.Security.SecurityException)
        {
            logger.LogDebug(ex, "The import could not read the file size for {DownloadPath}.", path);
        }

        throw new ImportSizeUnavailableException();
    }

    private long? ReadRecoverySize(string path)
    {
        try
        {
            return fileSystem.GetFileSize(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private async Task<bool> RecoverCapacityTransferAsync(Guid downloadId, CapacityRecovery recovery, Guid? userId,
        CancellationToken cancellationToken)
    {
        for (var i = recovery.Files.Count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = recovery.Files[i];
            try
            {
                var sourceSize = ReadRecoverySize(file.Source);
                var sourceSafe = sourceSize == file.Size && file.SourceIdentity is not null &&
                    fileSystem.GetFileIdentity(file.Source) == file.SourceIdentity && file.SourceVersion is not null &&
                    fileSystem.GetFileVersion(file.Source) == file.SourceVersion;
                var destinationSize = ReadRecoverySize(file.Destination);
                var destinationOwned = destinationSize is not null && file.Identity is not null &&
                    fileSystem.GetFileIdentity(file.Destination) == file.Identity && file
                        .PublishedVersion is not null &&
                    fileSystem.GetFileVersion(file.Destination) == file.PublishedVersion;
                var privateSize = file.PrivatePath is null ? null : ReadRecoverySize(file.PrivatePath);
                var privateOwned = privateSize is not null && file.Identity is not null &&
                    fileSystem.GetFileIdentity(file.PrivatePath!) == file.Identity;
                var ownedPath = privateOwned ? file.PrivatePath : destinationOwned ? file.Destination : null;
                var ownedSize = privateOwned ? privateSize : destinationOwned ? destinationSize : null;
                if (ownedPath is not null)
                {
                    if (sourceSafe)
                    {
                        fileSystem.DeleteFile(ownedPath);
                    }
                    else if (file.Mode == ImportMode.Move && sourceSize is null && ownedSize == file.Size &&
                        file.SourceVersion is not null && fileSystem.GetFileVersion(ownedPath) == file.SourceVersion &&
                        fileSystem.GetStorageCapacity(ownedPath)?.VolumeId is { } volume &&
                        volume == fileSystem.GetStorageCapacity(file.Source)?.VolumeId)
                    {
                        fileSystem.Move(ownedPath, file.Source);
                    }
                    else
                    {
                        continue;
                    }
                }
                else if (!sourceSafe)
                {
                    continue;
                }

                if (destinationSize is not null && (!destinationOwned || ownedPath != file.Destination))
                {
                    recovery.Files[i] = file with { PublishedVersion = null };
                    recovery.Reason = "File recovery is waiting because another file occupies the destination.";
                    await StoreCapacityRecoveryAsync(downloadId, recovery, userId, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "The import could not restore a transfer for download {DownloadId}.", downloadId);
                continue;
            }

            RemoveStagingDirectory(file);
            recovery.Files.RemoveAt(i);
            await StoreCapacityRecoveryAsync(downloadId, recovery, userId, cancellationToken).ConfigureAwait(false);
        }

        // Restore replacements only after every payload is safe and every destination is clear.
        if (recovery.Files.Count == 0)
        {
            for (var i = recovery.Backups.Count - 1; i >= 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var backup = recovery.Backups[i];
                try
                {
                    var backupSize = ReadRecoverySize(backup.BackupPath);
                    var originalSize = ReadRecoverySize(backup.OriginalPath);
                    if (backup.Identity is null)
                    {
                        continue;
                    }

                    if (backupSize is not null)
                    {
                        if (fileSystem.GetFileIdentity(backup.BackupPath) != backup.Identity ||
                            backup.Version is null || fileSystem.GetFileVersion(backup.BackupPath) != backup.Version ||
                            originalSize is not null)
                        {
                            continue;
                        }

                        fileSystem.Move(backup.BackupPath, backup.OriginalPath);
                    }
                    else if (originalSize != backup.OriginalSize ||
                        fileSystem.GetFileIdentity(backup.OriginalPath) != backup.Identity || backup.Version is null ||
                        fileSystem.GetFileVersion(backup.OriginalPath) != backup.Version)
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "The import could not restore a replacement for download {DownloadId}.",
                        downloadId);
                    continue;
                }

                recovery.Backups.RemoveAt(i);
                await StoreCapacityRecoveryAsync(downloadId, recovery, userId, cancellationToken).ConfigureAwait(false);
            }
        }

        return recovery.Files.Count == 0 && recovery.Backups.Count == 0;
    }

    private async Task<RollbackResult> RollbackTransfersAsync(IReadOnlyList<RecoveryFile> files,
        IReadOnlyList<TransferredOperation> transferred, IReadOnlyList<ReplaceBackup> backups,
        Guid downloadId, Guid? userId, string reason)
    {
        var recovery = new CapacityRecovery { Files = [.. files], Backups = [.. backups], Reason = reason };
        await StoreCapacityRecoveryAsync(downloadId, recovery, userId, CancellationToken.None).ConfigureAwait(false);
        await RecoverCapacityTransferAsync(downloadId, recovery, userId, CancellationToken.None).ConfigureAwait(false);
        var remaining = transferred.Where(entry => recovery.Files.Any(file =>
            file.Destination == entry.Operation.Destination)).ToList();
        return new RollbackResult(transferred.Count, transferred.Count - remaining.Count, backups.Count,
            backups.Count - recovery.Backups.Count, remaining, recovery.Backups);
    }

    private async Task CompleteCommittedImportAsync(
        Download download,
        CapacityRecovery recovery,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var downloadId = download.Id;
        var imports = recovery.Imports;
        if (imports.Count > 0)
        {
            recovery.Imports = [];
            try
            {
                await StoreCapacityRecoveryAsync(downloadId, recovery, userId, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                recovery.Imports = imports;
                logger.LogWarning(ex, "The import of download {DownloadId} committed, but the job could not " +
                    "record its imported content. The follow-up work will retry.", downloadId);
                return;
            }

            try
            {
                foreach (var imported in imports)
                {
                    await eventBus.PublishAsync(
                        ContentEventBuilder.BuildContentImportedEvent(imported.Impact, imported.UserId,
                            imported.FileCount) with
                        {
                            CorrelationId = imported.CorrelationId
                        },
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The import of download {DownloadId} committed, but the job could not " +
                    "publish its imported content.", downloadId);
            }
        }

        try
        {
            await FinishCommittedImportAsync(download, recovery, userId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "The import of download {DownloadId} committed, but its file cleanup did not " +
                "complete. The cleanup will retry.", downloadId);
        }
    }

    private async Task FinishCommittedImportAsync(
        Download download,
        CapacityRecovery recovery,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var downloadId = download.Id;
        var files = recovery.Files;
        var backups = recovery.Backups;
        foreach (var file in files)
        {
            RemoveStagingDirectory(file);
        }

        var complete = true;
        foreach (var superseded in recovery.Superseded)
        {
            try
            {
                using var paths = fileSystem.AcquireImportPaths([superseded.FilePath]);
                if (fileSystem.FileExists(superseded.FilePath))
                {
                    if (fileSystem.GetFileVersion(superseded.FilePath) != superseded.Version)
                    {
                        logger.LogWarning(
                            "The job kept {FilePath} for download {DownloadId} because the file changed after the " +
                            "import superseded it.",
                            superseded.FilePath,
                            downloadId);
                        continue;
                    }

                    fileSystem.DeleteFile(superseded.FilePath);
                }
                else
                {
                    FileSystem.RequireListable(fileSystem, superseded.RootPath);
                }
            }
            catch (Exception ex)
            {
                complete = false;
                logger.LogWarning(
                    ex,
                    "The job could not delete superseded file {FilePath} for download {DownloadId}.",
                    superseded.FilePath,
                    downloadId);
                await auditRecordRepository.CreateAsync(
                    new()
                    {
                        Category = AuditCategories.Content,
                        Operation = "FileDeleteFailed",
                        EntityType = nameof(MediaFile),
                        EntityId = superseded.MediaFileId.ToString(),
                        ParentEntityId = superseded.ContentHierarchyItemId.ToString(),
                        Detail = $"Could not delete the superseded file {superseded.FilePath}.",
                        Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                        UserId = userId
                    },
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    if (!await mediaFileRepository.DeleteAsync(superseded.MediaFileId, userId, ct)
                        .ConfigureAwait(false))
                    {
                        return;
                    }

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "SupersedeUpgrade",
                            EntityId = downloadId.ToString(),
                            EntityType = nameof(Download),
                            Detail = $"Removed previous file {superseded.FilePath} ({superseded.FileSize}) " +
                                $"after importing a quality upgrade for {download.Title}.",
                            Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                            UserId = userId
                        },
                        ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (files.Any(file => file.Identity is null || ReadRecoverySize(file.Destination) != file.Size ||
                    fileSystem.GetFileIdentity(file.Destination) != file.Identity || file.PublishedVersion is null ||
                    fileSystem.GetFileVersion(file.Destination) != file.PublishedVersion))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The job could not confirm imported file ownership for download {DownloadId}.",
                downloadId);
            return;
        }

        foreach (var file in files.Where(file => file.Mode == ImportMode.Move))
        {
            try
            {
                if (ReadRecoverySize(file.Source) is not null)
                {
                    var identity = fileSystem.GetFileIdentity(file.Source);
                    if (identity is null || file.SourceIdentity is null || identity != file.SourceIdentity ||
                        file.SourceVersion is null || fileSystem.GetFileVersion(file.Source) != file.SourceVersion)
                    {
                        complete = false;
                    }
                    else
                    {
                        fileSystem.DeleteFile(file.Source);
                    }
                }
            }
            catch (Exception ex)
            {
                complete = false;
                logger.LogWarning(ex, "The job could not clean the retained source for download {DownloadId}.",
                    downloadId);
            }
        }

        foreach (var backup in backups)
        {
            try
            {
                if (ReadRecoverySize(backup.BackupPath) is not null)
                {
                    if (backup.Identity is null || fileSystem.GetFileIdentity(backup.BackupPath) != backup.Identity ||
                        backup.Version is null || fileSystem.GetFileVersion(backup.BackupPath) != backup.Version)
                    {
                        complete = false;
                        continue;
                    }

                    fileSystem.DeleteFile(backup.BackupPath);
                }
            }
            catch (Exception ex)
            {
                complete = false;
                logger.LogWarning(ex, "The job could not clean the owned backup for download {DownloadId}.",
                    downloadId);
            }
        }

        if (complete)
        {
            await StoreCapacityRecoveryAsync(downloadId, new CapacityRecovery(), userId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<ItemImportGroup>> GroupByBoundItemAsync(
        ContentItem importTargetContentItem,
        IReadOnlyList<TransferredOperation> transferred,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var groups = new List<ItemImportGroup>();
        foreach (var files in transferred.GroupBy(entry => entry.Operation.ContentHierarchyItemId))
        {
            var contentItem = files.Key == importTargetContentItem.ContentHierarchyItemId
                ? importTargetContentItem
                : await contentService.GetOrCreateContentItemAsync(
                    files.Key,
                    timeProvider.GetUtcNow().UtcDateTime,
                    userId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            groups.Add(new ItemImportGroup(contentItem, [.. files]));
        }

        return groups;
    }

    private async Task PersistAndPublishAsync(
        Download download,
        ContentItem primaryContentItem,
        Release release,
        IReadOnlyList<ItemImportGroup> groups,
        IReadOnlyCollection<Guid> reconciledDownloadScopeNodeIds,
        IReadOnlyDictionary<string, ReplaceSnapshot> replaceSnapshots,
        RootFolder rootFolder,
        IReadOnlyList<string> skippedFiles,
        CapacityRecovery committed,
        Guid? userId,
        Guid? correlationId,
        Action onPersisted,
        CancellationToken cancellationToken)
    {
        var downloadId = download.Id;
        var totalFiles = groups.Sum(g => g.Files.Count);

        var resolvedGroups = new List<ResolvedItemImport>(groups.Count);
        foreach (var group in groups)
        {
            var qualityStatus = group.Files.Any(file => file.Operation.IsMainContent)
                ? await ResolveImportedQualityStatusAsync(release, group.ContentItem, downloadId, cancellationToken)
                    .ConfigureAwait(false)
                : group.ContentItem.QualityStatus;
            resolvedGroups.Add(new ResolvedItemImport(
                group.ContentItem,
                group.Files,
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
                rootFolder,
                skippedFiles,
                committed,
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

    private async Task PersistImportAsync(
        Download download,
        Guid primaryContentHierarchyItemId,
        IReadOnlyList<ResolvedItemImport> groups,
        IReadOnlyCollection<Guid> reconciledDownloadScopeNodeIds,
        IReadOnlyDictionary<string, ReplaceSnapshot> replaceSnapshots,
        RootFolder rootFolder,
        IReadOnlyList<string> skippedFiles,
        CapacityRecovery committed,
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
                var superseded = new List<SupersededFile>();

                foreach (var group in groups)
                {
                    var contentItem = group.ContentItem;
                    var supersedePriorFiles = group.Prior == FulfillmentStatus.Upgradable &&
                        rootFolder.UpgradeFileHandling == UpgradeFileHandling.Replace &&
                        group.Files.Any(file => file.Operation.IsMainContent);
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

                        var prior = hadReplaceSnapshot
                            ? snapshot.ExistingMediaFile
                            : await mediaFileRepository.GetByPathAsync(op.Destination, ct).ConfigureAwait(false);
                        if (!hadReplaceSnapshot && prior is not null && prior.ContentItemId != contentItem.Id)
                        {
                            throw new InvalidOperationException(
                                $"Destination file {op.Destination} belongs to content item {prior.ContentItemId}. " +
                                $"The job cannot import download {download.Id}");
                        }

                        if (prior is not null)
                        {
                            await mediaFileRepository
                                .UpdateAsync(
                                    prior with
                                    {
                                        FileKind = probe.FileKind.ToString(),
                                        MimeType = probe.MimeType,
                                        FileSize = probe.FileSize,
                                        FileModifiedAt = probe.LastWriteTimeUtc,
                                        ReplacedAt = now,
                                        DownloadId = download.Id,
                                        MediaInfo = op.MediaInfo,
                                        ModifiedAt = now
                                    },
                                    userId,
                                    ct)
                                .ConfigureAwait(false);
                            if (hadReplaceSnapshot)
                            {
                                replacedCount++;
                                var priorMediaInfo = prior.MediaInfo is null
                                    ? "null"
                                    : JsonSerializer.Serialize(prior.MediaInfo);
                                replaceDetailFragments.Add(
                                    $"{op.Destination} (prior size: {snapshot.PriorSize}, " +
                                    $"prior media: {priorMediaInfo})");
                            }

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
                                    FileModifiedAt = probe.LastWriteTimeUtc,
                                    MediaInfo = op.MediaInfo,
                                    DownloadId = download.Id,
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

                    superseded.AddRange(priorMediaFiles
                        .Where(m => !importedPaths.Contains(m.FilePath))
                        .Select(m => new SupersededFile(
                            m.Id,
                            contentItem.ContentHierarchyItemId,
                            rootFolder.Path,
                            m.FilePath,
                            m.FileSize,
                            fileSystem.GetFileVersion(m.FilePath))));

                    contentItem.QualityStatus = group.QualityStatus;
                    contentItem.ModifiedAt = now;
                    await contentRepository.UpdateAsync(contentItem, userId, ct).ConfigureAwait(false);

                    var fulfillmentChanged = await libraryImportService
                        .UpdateFulfillmentFromFilesAsync(contentItem.ContentHierarchyItemId, userId, ct)
                        .ConfigureAwait(false);
                    if (fulfillmentChanged)
                    {
                        var page = await contentRepository.QueryAsync(
                            new ContentItemQuery { ContentHierarchyItemId = contentItem.ContentHierarchyItemId },
                            new() { PageNumber = 1, PageSize = 2 },
                            null,
                            ct).ConfigureAwait(false);
                        var reconciledContentItem = page.Items.SingleOrDefault()
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

                committed.Superseded = superseded;
                committed.Imports =
                [
                    .. groups.Select(group => new ImportedContent(
                        new()
                        {
                            HierarchyItemId = group.ContentItem.ContentHierarchyItemId,
                            ItemId = group.ContentItem.Id,
                            Fulfillment = group.ContentItem.Fulfillment.ToString(),
                            WishlistId = group.ContentItem.RequestedViaWishlistId
                        },
                        group.Files.Count,
                        userId,
                        correlationId))
                ];
                await StoreCapacityRecoveryAsync(download.Id, committed, userId, ct).ConfigureAwait(false);

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
                        Detail = $"{transferVerb} {allFiles.Count} files for download {download.Title}" +
                            (skippedFiles.Count == 0
                                ? string.Empty
                                : $"; skipped {skippedFiles.Count} file(s): {string.Join("; ", skippedFiles)}"),
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
        var trimmedReason = statusReason.Length > MaxStatusReasonLength
            ? statusReason[..MaxStatusReasonLength]
            : statusReason;

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
        IReadOnlyList<string> skippedFiles,
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

                foreach (var skipped in skippedFiles)
                {
                    await auditRecordRepository.CreateAsync(
                        new AuditRecord
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "ImportDryRun",
                            EntityId = download.Id.ToString(),
                            EntityType = nameof(Download),
                            Detail = $"Dry run: skip {skipped}",
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
            DateTime? lastWriteTimeUtc = null;
            try
            {
                lastWriteTimeUtc = fileSystem.ReadFileState(filePath)?.LastWriteTimeUtc;
            }
            catch (NotSupportedException)
            {
                // Older extension file systems do not expose filesystem observations.
            }

            probes[filePath] = new MediaFileProbe(
                filePath,
                fileSystem.ResolveFileKind(mimeType),
                mimeType,
                ReadFileSize(filePath),
                lastWriteTimeUtc);
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
                Statuses = Enum.GetValues<DownloadStatus>().Except(DownloadStatuses.Finished).ToList()
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

        foreach (var releaseScope in releaseScopes.Where(scope => scope.Status == ReleaseScopeStatus.Matched &&
            hierarchyItemIds.Contains(scope.ContentHierarchyItemId)))
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
