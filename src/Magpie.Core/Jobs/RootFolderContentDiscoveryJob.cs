using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Content;
using Magpie.Sdk.Events;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class RootFolderContentDiscoveryJob(
    IRootFolderRepository rootFolderRepository,
    ILibraryImportCandidateRepository libraryImportCandidateRepository,
    IMediaFileRepository mediaFileRepository,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IContentTypeRegistry contentTypeRegistry,
    IJobManager jobManager,
    IFileSystem fileSystem,
    IEventBus eventBus,
    IProgressReporter progressReporter,
    TimeProvider timeProvider,
    ILogger<RootFolderContentDiscoveryJob> logger) : IJob
{
    private enum RootFolderIdSource
    {
        Parameter,
        EventContext,
    }

    private sealed record ScanPlan(string BasePath, IReadOnlyList<string> AcceptedFileTypes, bool GroupingEnabled);

    private sealed record PlannedFile(
        ScanPlan Plan,
        string FilePath,
        Dictionary<string, List<string>> Signals);

    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.RootFolderContentDiscovery,
        DisplayName = "Root folder content discovery",
        Description = "Discovers content in a root folder and creates library import candidates.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        Timeout = TimeSpan.FromHours(2),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTypeAndTarget,
        LockPartitionFields = [MagpieJobs.Parameters.RootFolderId],
        LockPartitionEventContextKeys = new Dictionary<string, string>
        {
            [MagpieJobs.Parameters.RootFolderId] = MagpieEvents.Keys.RootFolderId
        },
        Parameters =
        [
            new(MagpieJobs.Parameters.RootFolderId, "Required. The root folder to scan.", DataType.String, true, null)
        ],
    };

    private const int ListingProgressInterval = 1000;

    private static readonly TimeSpan StrandedImportReclaimThreshold = TimeSpan.FromHours(1);

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static Dictionary<string, List<string>> ToSignalDictionary(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? signals)
    {
        if (signals is null)
        {
            return [];
        }

        return signals.ToDictionary(
            pair => pair.Key,
            pair => (
                from value in pair.Value
                where !string.IsNullOrWhiteSpace(value)
                select value.Trim())
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasSampleDirectorySegment(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        return !string.IsNullOrEmpty(directory) && directory
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Contains("sample", StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasSearchableTitle(Dictionary<string, List<string>> signals) =>
        (signals.TryGetValue(ParsedFileSignalKeys.TitleToken, out var tokens) && tokens.Count > 0) ||
        (signals.TryGetValue(ParsedFileSignalKeys.Title, out var titles) && titles.Count > 0);

    private static bool HasMatchingTitle(
        Dictionary<string, List<string>> fileSignals,
        Dictionary<string, List<string>> releaseSignals)
    {
        var releaseTitles = releaseSignals
            .Where(pair => pair.Key is ParsedFileSignalKeys.Title or ParsedFileSignalKeys.TitleToken)
            .SelectMany(pair => pair.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return fileSignals
            .Where(pair => pair.Key is ParsedFileSignalKeys.Title or ParsedFileSignalKeys.TitleToken)
            .SelectMany(pair => pair.Value)
            .Any(releaseTitles.Contains);
    }

    private static bool TryResolveRootFolderId(JobContext context, out string raw, out RootFolderIdSource source)
    {
        if (context.Parameters.TryGetValue(MagpieJobs.Parameters.RootFolderId, out var rawParameter) &&
            !string.IsNullOrWhiteSpace(rawParameter))
        {
            raw = rawParameter;
            source = RootFolderIdSource.Parameter;
            return true;
        }

        if (context.Event is { Context: var eventContext } &&
            eventContext.TryGetValue(MagpieEvents.Keys.RootFolderId, out var rawFromEvent) &&
            !string.IsNullOrWhiteSpace(rawFromEvent))
        {
            raw = rawFromEvent;
            source = RootFolderIdSource.EventContext;
            return true;
        }

        raw = string.Empty;
        source = RootFolderIdSource.Parameter;
        return false;
    }

    private static bool IsLevelAtOrBelow(ContentTypeDefinition definition, string levelSlug, string rootLevelSlug)
    {
        var current = definition.LevelDefinitions.FirstOrDefault(level => level.Slug == levelSlug);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (current is not null && visited.Add(current.Slug))
        {
            if (current.Slug == rootLevelSlug)
            {
                return true;
            }

            current = definition.LevelDefinitions.FirstOrDefault(level => level.Slug == current.ParentSlug);
        }

        return false;
    }

    private List<ScanPlan> BuildRootLevelScanPlans(
        RootFolder rootFolder,
        IContentTypeDefinitionProvider provider,
        ContentTypeDefinition definition)
    {
        var pathProvider = provider as IPathResolutionContentTypeDefinitionProvider;
        var plans = new List<ScanPlan>();
        foreach (var root in definition.LevelDefinitions.Where(level => level.ParentSlug is null))
        {
            var basePath = string.IsNullOrWhiteSpace(root.Subfolder)
                ? rootFolder.Path
                : Path.Combine(rootFolder.Path, root.Subfolder);
            var accepted = definition.LevelDefinitions
                .Where(level => IsLevelAtOrBelow(definition, level.Slug, root.Slug))
                .SelectMany(level => level.AcceptedFileTypes)
                .Where(fileType => !string.IsNullOrWhiteSpace(fileType))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (accepted.Length > 0)
            {
                var chain = pathProvider?.ResolveDirectoryLevelChain(FindLeafLevelSlug(definition.Slug, root.Slug));
                plans.Add(new ScanPlan(basePath, accepted, chain is { Count: > 0 }));
            }
        }

        return plans;
    }

    private string FindLeafLevelSlug(string contentType, string levelSlug)
    {
        var current = levelSlug;
        while (true)
        {
            var children = contentTypeRegistry.GetChildLevels(contentType, current);
            if (children.Count != 1)
            {
                return current;
            }

            current = children[0].Slug;
        }
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        if (!TryResolveRootFolderId(context, out var raw, out var source))
        {
            throw new ArgumentException("A root folder ID is required.");
        }

        if (!Guid.TryParse(raw, out var rootFolderId))
        {
            throw new ArgumentException(
                source is RootFolderIdSource.EventContext
                    ? "The job received an invalid root folder ID."
                    : "The root folder ID has an invalid format.");
        }

        await ScanAsync(
            rootFolderId,
            context.ManualTriggerUserId,
            context.CorrelationId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ScanAsync(
        Guid rootFolderId,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        progressReporter.Report(0, "The scan will load the root folder.");

        var rootFolder = await rootFolderRepository.GetByIdAsync(rootFolderId, cancellationToken)
            .ConfigureAwait(false);
        if (rootFolder is null)
        {
            throw new InvalidOperationException($"Root folder {rootFolderId} does not exist.");
        }

        var provider = contentTypeRegistry.FindProviderFor(rootFolder.ContentType);
        if (provider is null)
        {
            logger.LogWarning(
                "No definition provider exists for {ContentType}, so no scan ran",
                rootFolder.ContentType);
            progressReporter.Report(1, "The scan stopped because the content type has no definition provider.");
            return;
        }

        var definition = provider.Definition;
        var plans = BuildRootLevelScanPlans(rootFolder, provider, definition);
        if (plans.Count == 0)
        {
            logger.LogWarning("{ContentType} has no accepted file types, so no scan ran", rootFolder.ContentType);
            progressReporter.Report(1, "The scan stopped because the content type accepts no file types.");
            return;
        }

        var scanRunId = Guid.NewGuid();
        logger.LogInformation(
            "The job will start scan {ScanRunId} of root folder {RootFolderId}",
            scanRunId,
            rootFolderId);

        await ReclaimStrandedImportsAsync(rootFolderId, userId, cancellationToken).ConfigureAwait(false);

        await libraryImportCandidateRepository
            .DeleteByRootFolderAsync(rootFolderId, userId, cancellationToken)
            .ConfigureAwait(false);

        var inFlightPaths = await LoadInFlightPathsAsync(rootFolderId, cancellationToken).ConfigureAwait(false);
        var linkedPaths = await LoadLinkedPathsAsync(
            plans.Select(plan => plan.BasePath),
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progressReporter.Report(null, "The scan will list the files.");

        var planned = new List<PlannedFile>();
        var seenPaths = new HashSet<string>(PathComparer);
        var listedCount = 0;
        foreach (var plan in plans)
        {
            if (!fileSystem.DirectoryExists(plan.BasePath))
            {
                logger.LogInformation(
                    "Root folder {RootFolderId} has no folder at {BasePath}, so the scan skipped it",
                    rootFolderId,
                    plan.BasePath);
                continue;
            }

            try
            {
                foreach (var file in fileSystem.EnumerateFiles(plan.BasePath))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    listedCount++;
                    if (listedCount % ListingProgressInterval == 0)
                    {
                        progressReporter.Report(null, $"The scan has listed {listedCount} file(s).");
                    }

                    if (!seenPaths.Add(fileSystem.GetFullPath(file)))
                    {
                        continue;
                    }

                    if (HasSampleDirectorySegment(file) ||
                        !await fileSystem
                            .MatchesAnyFileTypeAsync(file, plan.AcceptedFileTypes, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        continue;
                    }

                    planned.Add(new(plan, file, ToSignalDictionary(provider.Parse(file, out _))));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "The job failed to enumerate files under {BasePath} of root folder {RootFolderId}",
                    plan.BasePath,
                    rootFolderId);
                throw;
            }
        }

        var selected = new List<PlannedFile>(planned.Count);
        foreach (var group in planned.GroupBy(
                     file => ComputeReleaseKey(file.Plan, file.FilePath),
                     PathComparer))
        {
            var releaseSignals = ToSignalDictionary(provider.Parse(group.Key ?? string.Empty, out _));
            var matched = group.Where(file => HasMatchingTitle(file.Signals, releaseSignals)).ToList();
            var kept = matched;
            if (kept.Count == 0)
            {
                kept = group.Where(file => HasSearchableTitle(file.Signals)).ToList();
            }

            if (kept.Count == 0)
            {
                kept = group.ToList();
            }

            foreach (var dropped in group.Where(file => !kept.Contains(file)))
            {
                logger.LogDebug(
                    "The scan skipped {FilePath} because its title does not match release {ReleaseKey}",
                    dropped.FilePath,
                    group.Key);
            }

            selected.AddRange(kept);
        }

        var total = selected.Count;
        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selectedFile = selected[i];
            var plan = selectedFile.Plan;
            var file = selectedFile.FilePath;
            progressReporter.ReportFraction(i, total, $"The scan will examine {Path.GetFileName(file)}.");

            try
            {
                await ProcessFileAsync(
                    rootFolder,
                    scanRunId,
                    file,
                    ComputeLayoutKey(plan, file),
                    selectedFile.Signals,
                    inFlightPaths,
                    linkedPaths,
                    userId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The job failed to process {FilePath} during scan {ScanRunId}", file, scanRunId);
                await TryPersistFailureAsync(
                    rootFolder,
                    scanRunId,
                    file,
                    ex,
                    userId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (await HasUnresolvedCandidatesAsync(cancellationToken).ConfigureAwait(false))
        {
            await EnqueueMatchResolutionAsync(userId, correlationId, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var summary = $"Scan of {total} file(s) complete.";
            await eventBus
                .PublishAsync(
                    ContentEventBuilder.BuildLibraryScanCompletedEvent(rootFolderId, summary) with
                    {
                        CorrelationId = correlationId
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The job failed to publish library.scan.completed for {RootFolderId}", rootFolderId);
        }

        progressReporter.Report(1, "The root-folder scan completed.");
    }

    private async Task ReclaimStrandedImportsAsync(Guid rootFolderId, Guid? userId, CancellationToken cancellationToken)
    {
        var inFlight = await libraryImportCandidateRepository.QueryAllAsync(
            new LibraryImportCandidateQuery
            {
                RootFolderId = rootFolderId,
                Statuses = [LibraryImportCandidateStatus.Queued, LibraryImportCandidateStatus.Importing]
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var cutoff = timeProvider.GetUtcNow().UtcDateTime - StrandedImportReclaimThreshold;
        foreach (var candidate in inFlight)
        {
            var lastActivity = candidate.ModifiedAt ?? candidate.CreatedAt;
            if (lastActivity > cutoff)
            {
                continue;
            }

            var reclaimed = await libraryImportCandidateRepository
                .MarkImportFailedAsync(
                    candidate.Id,
                    "Import did not complete within the reclaim window and was reset so the file can be rescanned.",
                    userId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (reclaimed)
            {
                logger.LogWarning(
                    "The job reclaimed stranded library import candidate {LibraryImportCandidateId} " +
                    "({FilePath}), stuck in {CandidateStatus} status since {LastActivity:o}",
                    candidate.Id,
                    candidate.FilePath,
                    candidate.Status,
                    lastActivity);
            }
        }
    }

    private async Task<IReadOnlySet<string>> LoadInFlightPathsAsync(
        Guid rootFolderId,
        CancellationToken cancellationToken)
    {
        var candidates = await libraryImportCandidateRepository.QueryAllAsync(
            new LibraryImportCandidateQuery
            {
                RootFolderId = rootFolderId,
                Statuses = [LibraryImportCandidateStatus.Queued, LibraryImportCandidateStatus.Importing]
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var paths = new HashSet<string>(PathComparer);
        foreach (var candidate in candidates)
        {
            paths.Add(fileSystem.GetFullPath(candidate.FilePath));
        }

        return paths;
    }

    private async Task<IReadOnlySet<string>> LoadLinkedPathsAsync(
        IEnumerable<string> basePaths,
        CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(PathComparer);
        foreach (var basePath in basePaths)
        {
            var prefix = fileSystem.GetFullPath(basePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var linked = await mediaFileRepository.QueryAllAsync(
                new MediaFileQuery { FilePathPrefix = prefix },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var item in linked)
            {
                paths.Add(fileSystem.GetFullPath(item.FilePath));
            }
        }

        return paths;
    }

    private string? ComputeLayoutKey(ScanPlan plan, string filePath)
    {
        if (!plan.GroupingEnabled)
        {
            return null;
        }

        var relative = Path.GetRelativePath(plan.BasePath, filePath);
        var separatorIndex = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        if (separatorIndex <= 0)
        {
            return null;
        }

        return fileSystem.GetFullPath(Path.Combine(plan.BasePath, relative[..separatorIndex]));
    }

    private string ComputeReleaseKey(ScanPlan plan, string filePath)
    {
        var relative = Path.GetRelativePath(plan.BasePath, filePath);
        var separatorIndex = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return separatorIndex <= 0
            ? fileSystem.GetFullPath(filePath)
            : fileSystem.GetFullPath(Path.Combine(plan.BasePath, relative[..separatorIndex]));
    }

    private async Task ProcessFileAsync(
        RootFolder rootFolder,
        Guid scanRunId,
        string filePath,
        string? layoutKey,
        Dictionary<string, List<string>> signals,
        IReadOnlySet<string> inFlightPaths,
        IReadOnlySet<string> linkedPaths,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        if (inFlightPaths.Contains(fileSystem.GetFullPath(filePath)))
        {
            return;
        }

        if (linkedPaths.Contains(fileSystem.GetFullPath(filePath)))
        {
            return;
        }

        var needsMatchResolution = layoutKey is not null || HasSearchableTitle(signals);
        var status = needsMatchResolution
            ? LibraryImportCandidateStatus.MatchingPending
            : LibraryImportCandidateStatus.Pending;
        var quarantineReason = needsMatchResolution ? null : "The filename contains no title.";

        var importCandidate = new LibraryImportCandidate
        {
            RootFolderId = rootFolder.Id,
            ScanRunId = scanRunId,
            FilePath = filePath,
            ContentType = rootFolder.ContentType,
            Status = status,
            ParsedSignals = signals,
            LayoutKey = layoutKey,
            Matches = [],
            Notes = quarantineReason
        };

        await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                await libraryImportCandidateRepository.CreateAsync(importCandidate, userId, ct)
                    .ConfigureAwait(false);

                await WriteAuditAsync(
                    operation: "FileScanned",
                    entityType: nameof(LibraryImportCandidate),
                    entityId: importCandidate.Id.ToString(),
                    detail: needsMatchResolution
                        ? $"Scan queued {Path.GetFileName(filePath)} for match resolution."
                        : $"Scan held {Path.GetFileName(filePath)} for manual resolve: {quarantineReason}",
                    userId,
                    correlationId,
                    ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> HasUnresolvedCandidatesAsync(CancellationToken cancellationToken)
    {
        var page = await libraryImportCandidateRepository.QueryAsync(
            new LibraryImportCandidateQuery { Status = LibraryImportCandidateStatus.MatchingPending },
            new() { PageNumber = 1, PageSize = 1 },
            sort: null,
            cancellationToken).ConfigureAwait(false);
        return page.TotalItems > 0;
    }

    private async Task EnqueueMatchResolutionAsync(
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await jobManager.ExecuteAsync(
                new JobEnqueueRequest
                {
                    JobType = MagpieJobs.Types.LibraryImportMatchResolution,
                    Mode = JobExecutionMode.Queue,
                    UserId = userId,
                    CorrelationId = correlationId,
                },
                cancellationToken).ConfigureAwait(false);
            logger.LogInformation("The scan queued a library import match resolution run.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The scan failed to queue a library import match resolution run.");
        }
    }

    private async Task TryPersistFailureAsync(
        RootFolder rootFolder,
        Guid scanRunId,
        string filePath,
        Exception ex,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var failure = new LibraryImportCandidate
            {
                RootFolderId = rootFolder.Id,
                ScanRunId = scanRunId,
                FilePath = filePath,
                ContentType = rootFolder.ContentType,
                Status = LibraryImportCandidateStatus.Failed,
                Notes = $"Scan failed: {ex.Message}"
            };
            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    await libraryImportCandidateRepository.CreateAsync(failure, userId, ct)
                        .ConfigureAwait(false);

                    await WriteAuditAsync(
                        operation: "FileErrored",
                        entityType: nameof(LibraryImportCandidate),
                        entityId: failure.Id.ToString(),
                        detail: $"Scan failed for {Path.GetFileName(filePath)}: {ex.Message}",
                        userId,
                        correlationId,
                        ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception writeEx)
        {
            logger.LogWarning(writeEx, "The job failed to save the failure row for {FilePath}", filePath);
        }
    }

    private async Task WriteAuditAsync(
        string operation,
        string? entityType,
        string? entityId,
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
}
