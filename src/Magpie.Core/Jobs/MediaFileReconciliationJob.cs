using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Storage;
using Magpie.Sdk.Events;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Media;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class MediaFileReconciliationJob(
    IMediaFileRepository mediaFileRepository,
    ILibraryImportCandidateRepository libraryImportCandidateRepository,
    IContentRepository contentRepository,
    IContentHierarchyItemRepository hierarchyRepository,
    IRootFolderRepository rootFolderRepository,
    IContentTypeRegistry contentTypeRegistry,
    IContentService contentService,
    IReleaseEvaluationService releaseEvaluationService,
    IFileSystem fileSystem,
    ITransactionManager transactionManager,
    IAuditRecordRepository auditRecordRepository,
    IJobManager jobManager,
    IProgressReporter progressReporter,
    TimeProvider timeProvider,
    ILogger<MediaFileReconciliationJob> logger,
    IMediaInfoExtractor? mediaInfoExtractor = null) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.MediaFileReconciliation,
        DisplayName = "Media file reconciliation",
        Description = "Checks library files for missing or replaced media and updates the library.",
        Category = "Content",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters =
        [
            new(
                MagpieJobs.Parameters.RootFolderId,
                "The root folder to inspect, or empty for all roots.",
                DataType.String,
                false,
                null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            },
            new(
                MagpieJobs.Parameters.ContentHierarchyItemId,
                "The item to inspect, or empty for the library.",
                DataType.String,
                false,
                null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            }
        ],
    };

    private const double RecodeDurationToleranceSeconds = 2d;

    private static readonly StringComparison NameComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record RenamedFile(string Path, FileState State, MediaInfo? MediaInfo, bool Recoded);

    private static bool IsSameDuration(MediaInfo? before, MediaInfo? after, double toleranceSeconds) =>
        before?.DurationSeconds is { } expected && after?.DurationSeconds is { } actual &&
        Math.Abs(expected - actual) <= toleranceSeconds;

    private static bool IsSameWriteTime(DateTime? stored, DateTime actual) =>
        stored is { } value && Math.Abs(value.Ticks - actual.Ticks) < TimeSpan.TicksPerMicrosecond;

    private static Guid? ReadId(JobContext context, string parameter, string eventKey)
    {
        context.Parameters.TryGetValue(parameter, out var value);
        if (string.IsNullOrWhiteSpace(value) && context.Event is { } triggeredEvent)
        {
            triggeredEvent.Context.TryGetValue(eventKey, out value);
        }

        return string.IsNullOrWhiteSpace(value) ? null : Guid.TryParse(value, out var id)
            ? id : throw new ArgumentException($"Parameter {parameter} requires a valid ID.");
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        var rootId = ReadId(context, MagpieJobs.Parameters.RootFolderId, MagpieEvents.Keys.RootFolderId);
        var targetId = ReadId(
            context,
            MagpieJobs.Parameters.ContentHierarchyItemId,
            MagpieEvents.Keys.ContentHierarchyItemId);
        if (context.EventTriggerId is not null && rootId is null && targetId is null)
        {
            throw new ArgumentException("A media reconciliation event requires a root folder or content item.");
        }

        var files = await mediaFileRepository.QueryAllAsync(
            new MediaFileQuery { ContentHierarchyItemId = targetId },
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var contentIds = files.Select(file => file.ContentItemId).Distinct().ToHashSet();
        var filesByContent = files.ToLookup(file => file.ContentItemId);
        var previouslyReconciled = await contentRepository.QueryAllAsync(
            new ContentItemQuery { ContentHierarchyItemId = targetId },
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        contentIds.UnionWith(previouslyReconciled
            .Where(item => item.MediaReconciledAt is not null).Select(item => item.Id));
        contentIds.UnionWith(files.SelectMany(file => file.Coverage).Select(coverage => coverage.ContentItemId));
        var inspected = 0;
        var changed = 0;
        var failed = 0;
        var queuedDiscoveries = new HashSet<Guid>();
        foreach (var contentId in contentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await contentRepository.GetByIdAsync(contentId, cancellationToken).ConfigureAwait(false);
            var node = content is null ? null : await hierarchyRepository
                .GetByIdAsync(content.ContentHierarchyItemId, cancellationToken).ConfigureAwait(false);
            if (node is null || rootId is { } requestedRoot && node.EffectiveRootFolderId != requestedRoot)
            {
                continue;
            }

            var root = await rootFolderRepository.GetByIdAsync(node.EffectiveRootFolderId, cancellationToken)
                .ConfigureAwait(false);
            if (root is null)
            {
                failed++;
                logger.LogWarning(
                    "Media reconciliation kept item {ItemId} because its root folder is unavailable",
                    node.Id);
                continue;
            }

            foreach (var file in filesByContent[contentId])
            {
                try
                {
                    changed += await ReconcileFileAsync(
                        node,
                        root,
                        file,
                        queuedDiscoveries,
                        context,
                        cancellationToken).ConfigureAwait(false)
                        ? 1 : 0;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    logger.LogWarning(
                        ex,
                        "Media reconciliation kept file {MediaFileId} for another inspection",
                        file.Id);
                }

                inspected++;
                progressReporter.ReportFraction(inspected, files.Count);
            }

            try
            {
                await RefreshStateAsync(node.Id, context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(ex, "Media reconciliation could not update the state of {ItemId}", node.Id);
            }
        }

        progressReporter.Report(1, $"Inspected {inspected} files; changed {changed}; deferred {failed} inspections.");
    }

    private FileState? InspectFile(string path, string rootPath)
    {
        if (!fileSystem.IsPathWithin(rootPath, path))
        {
            throw new IOException("The media file is outside its configured root folder.");
        }

        using (var entries = fileSystem.EnumerateFileSystemEntries(rootPath).GetEnumerator())
        {
            entries.MoveNext();
        }

        try
        {
            return fileSystem.ReadFileState(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private async Task<bool> ReconcileFileAsync(
        ContentHierarchyItem node,
        RootFolder root,
        MediaFile file,
        ISet<Guid> queuedDiscoveries,
        JobContext context,
        CancellationToken cancellationToken)
    {
        var state = InspectFile(file.FilePath, root.Path);
        var (renamed, hasUnattachedFile) = state is null && IsPrimary(node, file)
            ? await FindRenamedFileAsync(node, root, file, cancellationToken).ConfigureAwait(false)
            : (null, false);
        var replacement = state is not null &&
            (state.Size != file.FileSize ||
                file.FileModifiedAt is not null && !IsSameWriteTime(file.FileModifiedAt, state.LastWriteTimeUtc));
        var primary = IsPrimary(node, file);
        var needsAnalysis = state is not null && primary &&
            (replacement || file.MediaInfo is null || file.FileModifiedAt is null);
        var mediaInfo = replacement || needsAnalysis ? null : file.MediaInfo;
        if (needsAnalysis && mediaInfoExtractor is not null)
        {
            mediaInfo = await mediaInfoExtractor.ExtractAsync(file.FilePath, cancellationToken).ConfigureAwait(false);
        }

        if (state is not null && state != InspectFile(file.FilePath, root.Path))
        {
            throw new IOException("The media file changed while its information was read.");
        }

        if (state is not null && !replacement && IsSameWriteTime(file.FileModifiedAt, state.LastWriteTimeUtc) &&
            file.FileSize == state.Size && (!needsAnalysis || mediaInfo is null))
        {
            return false;
        }

        using var paths = state is null ? fileSystem.AcquireImportPaths([file.FilePath]) : null;
        return await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var lineage = await hierarchyRepository.GetLineageAsync(node.Id, ct).ConfigureAwait(false);
                if (lineage.Count == 0)
                {
                    return false;
                }

                await hierarchyRepository.LockAsync(lineage[^1].Id, ct).ConfigureAwait(false);
                var current = await mediaFileRepository.GetByIdAsync(file.Id, ct).ConfigureAwait(false);
                if (current is null || current.ContentItemId != file.ContentItemId ||
                    !fileSystem.PathsEqual(current.FilePath, file.FilePath) ||
                    current.FileSize != file.FileSize ||
                    current.FileModifiedAt != file.FileModifiedAt ||
                    current.ReplacedAt != file.ReplacedAt)
                {
                    return false;
                }

                if (state != InspectFile(current.FilePath, root.Path))
                {
                    throw new IOException("The media file changed before reconciliation could save its state.");
                }

                if (state is null && renamed is not null &&
                    await ReattachAsync(node, current, renamed, context, ct).ConfigureAwait(false))
                {
                    return true;
                }

                if (state is null)
                {
                    if (!await mediaFileRepository.DeleteAsync(current.Id, context.ManualTriggerUserId, ct)
                        .ConfigureAwait(false))
                    {
                        return false;
                    }

                    await auditRecordRepository.CreateAsync(
                        new AuditRecord
                        {
                            Category = AuditCategories.Content,
                            Operation = "MediaFileDeleted",
                            EntityType = nameof(MediaFile),
                            EntityId = file.Id.ToString(),
                            ParentEntityId = node.Id.ToString(),
                            Detail = $"Deleted the record of {file.FilePath} because the file no longer exists.",
                            Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                            UserId = context.ManualTriggerUserId,
                            CorrelationId = context.CorrelationId
                        },
                        ct).ConfigureAwait(false);

                    var topLevelId = lineage[^1].Id;
                    if (hasUnattachedFile)
                    {
                        await transactionManager.OnCommittedAsync(
                            token => queuedDiscoveries.Add(topLevelId)
                                ? QueueDiscoveryAsync(root, topLevelId, context, token)
                                : Task.CompletedTask,
                            ct).ConfigureAwait(false);
                    }

                    return true;
                }

                var now = timeProvider.GetUtcNow().UtcDateTime;
                var mimeType = await fileSystem.LookupMimeTypeAsync(current.FilePath, ct).ConfigureAwait(false);
                var updated = await mediaFileRepository.UpdateAsync(
                    current with
                    {
                        FileSize = state.Size,
                        FileModifiedAt = state.LastWriteTimeUtc,
                        ReplacedAt = replacement ? now : current.ReplacedAt,
                        DownloadId = replacement ? null : current.DownloadId,
                        MediaInfo = mediaInfo,
                        MimeType = mimeType ?? current.MimeType,
                        FileKind = mimeType is null
                            ? current.FileKind
                            : fileSystem.ResolveFileKind(mimeType).ToString(),
                        ModifiedAt = now
                    },
                    context.ManualTriggerUserId,
                    ct).ConfigureAwait(false);
                if (!updated)
                {
                    throw new IOException("The stored file changed before reconciliation could save its state.");
                }

                if (replacement)
                {
                    await auditRecordRepository.CreateAsync(
                        new AuditRecord
                        {
                            Category = AuditCategories.Content,
                            Operation = "MediaFileReplaced",
                            EntityType = nameof(MediaFile),
                            EntityId = file.Id.ToString(),
                            ParentEntityId = node.Id.ToString(),
                            Detail = $"Reconciled replacement contents at {file.FilePath}.",
                            Timestamp = now,
                            UserId = context.ManualTriggerUserId,
                            CorrelationId = context.CorrelationId
                        },
                        ct).ConfigureAwait(false);
                }

                await RefreshStateAsync(node.Id, context, ct).ConfigureAwait(false);
                return replacement;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<(RenamedFile? Renamed, bool HasUnattachedFile)> FindRenamedFileAsync(
        ContentHierarchyItem node,
        RootFolder root,
        MediaFile file,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(file.FilePath);
        if (directory is null || !fileSystem.IsPathWithin(root.Path, directory) ||
            !fileSystem.DirectoryExists(directory))
        {
            return (null, false);
        }

        var baseName = Path.GetFileNameWithoutExtension(file.FilePath);
        var found = new List<RenamedFile>();
        var hasUnattachedFile = false;
        var otherFiles = new List<string>();
        foreach (var path in fileSystem.EnumerateFileSystemEntries(directory).Where(fileSystem.FileExists))
        {
            var recoded = string.Equals(Path.GetFileNameWithoutExtension(path), baseName, NameComparison);
            var state = fileSystem.ReadFileState(path);
            if (!recoded && (state.Size != file.FileSize ||
                !IsSameWriteTime(file.FileModifiedAt, state.LastWriteTimeUtc)))
            {
                otherFiles.Add(path);
                continue;
            }

            if (!await IsUnattachedPrimaryAsync(node, file, path, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            hasUnattachedFile = true;

            var mediaInfo = recoded && mediaInfoExtractor is not null
                ? await mediaInfoExtractor.ExtractAsync(path, cancellationToken).ConfigureAwait(false)
                : null;
            if (!recoded || file.MediaInfo?.DurationSeconds is null || mediaInfoExtractor is null ||
                IsSameDuration(file.MediaInfo, mediaInfo, RecodeDurationToleranceSeconds))
            {
                found.Add(new RenamedFile(path, state, mediaInfo, recoded));
            }
        }

        var renamed = found.Count == 1 ? found[0] : null;
        foreach (var path in renamed is null && !hasUnattachedFile ? otherFiles : [])
        {
            if (await IsUnattachedPrimaryAsync(node, file, path, cancellationToken).ConfigureAwait(false))
            {
                hasUnattachedFile = true;
                break;
            }
        }

        return (renamed, hasUnattachedFile);
    }

    private async Task<bool> IsUnattachedPrimaryAsync(
        ContentHierarchyItem node,
        MediaFile file,
        string path,
        CancellationToken cancellationToken)
    {
        var mimeType = await fileSystem.LookupMimeTypeAsync(path, cancellationToken).ConfigureAwait(false);
        return IsPrimary(node, file with { FilePath = path, MimeType = mimeType }) &&
            await mediaFileRepository.GetByPathAsync(path, cancellationToken).ConfigureAwait(false) is null;
    }

    private async Task QueueDiscoveryAsync(
        RootFolder root,
        Guid contentHierarchyItemId,
        JobContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await jobManager.ExecuteAsync(
                new JobEnqueueRequest
                {
                    JobType = MagpieJobs.Types.RootFolderContentDiscovery,
                    Parameters = new Dictionary<string, string>
                    {
                        [MagpieJobs.Parameters.RootFolderId] = root.Id.ToString("D"),
                        [MagpieJobs.Parameters.ContentHierarchyItemId] = contentHierarchyItemId.ToString("D")
                    },
                    Mode = JobExecutionMode.Queue,
                    UserId = context.ManualTriggerUserId,
                    CorrelationId = context.CorrelationId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Media reconciliation could not queue discovery for item {ItemId}",
                contentHierarchyItemId);
        }
    }

    private async Task<bool> ReattachAsync(
        ContentHierarchyItem node,
        MediaFile current,
        RenamedFile renamed,
        JobContext context,
        CancellationToken cancellationToken)
    {
        var attached = await mediaFileRepository.GetByPathAsync(renamed.Path, cancellationToken)
            .ConfigureAwait(false);
        if (attached is not null || fileSystem.ReadFileState(renamed.Path) != renamed.State)
        {
            return false;
        }

        var rejected = await libraryImportCandidateRepository.QueryAllAsync(
            new LibraryImportCandidateQuery
            {
                RootFolderId = node.EffectiveRootFolderId,
                Status = LibraryImportCandidateStatus.Rejected
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (rejected.Any(candidate => fileSystem.PathsEqual(candidate.FilePath, renamed.Path)))
        {
            return false;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var mimeType = await fileSystem.LookupMimeTypeAsync(renamed.Path, cancellationToken).ConfigureAwait(false);
        var updated = await mediaFileRepository.UpdateAsync(
            current with
            {
                FilePath = renamed.Path,
                FileSize = renamed.State.Size,
                FileModifiedAt = renamed.State.LastWriteTimeUtc,
                ReplacedAt = renamed.Recoded ? now : current.ReplacedAt,
                DownloadId = renamed.Recoded ? null : current.DownloadId,
                MediaInfo = renamed.MediaInfo ?? (renamed.Recoded ? null : current.MediaInfo),
                MimeType = mimeType ?? current.MimeType,
                FileKind = mimeType is null ? current.FileKind : fileSystem.ResolveFileKind(mimeType).ToString(),
                ModifiedAt = now
            },
            context.ManualTriggerUserId,
            cancellationToken).ConfigureAwait(false);
        if (!updated)
        {
            throw new IOException("The stored file changed before reconciliation could reattach it.");
        }

        await auditRecordRepository.CreateAsync(
            new AuditRecord
            {
                Category = AuditCategories.Content,
                Operation = renamed.Recoded ? "MediaFileRecoded" : "MediaFileRenamed",
                EntityType = nameof(MediaFile),
                EntityId = current.Id.ToString(),
                ParentEntityId = node.Id.ToString(),
                Detail = $"Reattached {current.FilePath} to {renamed.Path}.",
                Timestamp = now,
                UserId = context.ManualTriggerUserId,
                CorrelationId = context.CorrelationId
            },
            cancellationToken).ConfigureAwait(false);
        await RefreshStateAsync(node.Id, context, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private bool IsPrimary(ContentHierarchyItem node, MediaFile file)
    {
        var level = contentTypeRegistry.GetDefinitionFor(node.ContentType)?.LevelDefinitions
            .FirstOrDefault(candidate => candidate.Slug == node.Level);
        if (level is null)
        {
            throw new InvalidOperationException("The content level has no file contract.");
        }

        return FileSystem.IsPrimaryFile(fileSystem, level, file);
    }

    private Task RefreshStateAsync(Guid hierarchyId, JobContext context, CancellationToken cancellationToken)
    {
        return transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var lineage = await hierarchyRepository.GetLineageAsync(hierarchyId, ct).ConfigureAwait(false);
                if (lineage.Count == 0)
                {
                    return;
                }

                await hierarchyRepository.LockAsync(lineage[^1].Id, ct).ConfigureAwait(false);
                foreach (var ancestor in lineage)
                {
                    var node = await hierarchyRepository.GetByIdAsync(ancestor.Id, ct).ConfigureAwait(false);
                    if (node is null)
                    {
                        continue;
                    }

                    var files = (node.ContentItem?.MediaFiles ?? [])
                        .Concat(node.ContentItem?.CoveringMediaFiles ?? [])
                        .Where(file => IsPrimary(node, file))
                        .ToList();

                    var now = timeProvider.GetUtcNow().UtcDateTime;
                    var current = node.ContentItem ?? await contentService.GetOrCreateContentItemAsync(
                        node.Id,
                        now,
                        context.ManualTriggerUserId,
                        cancellationToken: ct).ConfigureAwait(false);
                    var (quality, rank) = await releaseEvaluationService
                        .ScoreMediaQualityAsync(files, node.EffectiveQualityTierListId, ct)
                        .ConfigureAwait(false);
                    if (current.QualityStatus == quality && current.CurrentFileQualityRank == rank)
                    {
                        continue;
                    }

                    await contentRepository.UpdateMediaStateAsync(
                        current with
                        {
                            QualityStatus = quality,
                            CurrentFileQualityRank = rank,
                            ModifiedAt = now
                        },
                        context.ManualTriggerUserId,
                        ct).ConfigureAwait(false);
                }

                await contentRepository.ReconcileMediaStateAsync(hierarchyId, context.ManualTriggerUserId, ct)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }
}
